using System.Collections.Concurrent;
using System.Net;
using Assistant.Net.Configuration;
using Assistant.Net.Services.Music.Logic;
using Assistant.Net.Utilities.Ui;
using Discord;
using Discord.Interactions;
using Discord.Net;
using Discord.WebSocket;
using Lavalink4NET.Clients;
using Lavalink4NET.Players;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Services.Music;

public class NowPlayingService : IDisposable
{
    // Constants for Custom IDs
    public const string NpCustomIdPrefix = "assistant:np";

    private readonly ConcurrentDictionary<ulong, NowPlayingMessageInfo> _activeNowPlayingMessages = new();

    // Dependencies
    private readonly DiscordSocketClient _client;
    private readonly Config _config;
    private readonly ILogger<NowPlayingService> _logger;
    private readonly MusicService _musicService;
    private readonly TimeSpan _updateInterval = TimeSpan.FromSeconds(5);

    public NowPlayingService(
        DiscordSocketClient client,
        MusicService musicService,
        Config config,
        ILogger<NowPlayingService> logger)
    {
        _client = client;
        _musicService = musicService;
        _config = config;
        _logger = logger;

        // Hook into player events to remove NP message when player stops
        _musicService.PlayerStopped += OnPlayerStoppedAsync;
        _musicService.QueueEmptied += OnQueueEmptiedAsync;

        _logger.LogInformation("NowPlayingService initialized.");
    }

    public void Dispose()
    {
        _logger.LogInformation("Disposing NowPlayingService and clearing active messages.");
        foreach (var guildId in _activeNowPlayingMessages.Keys.ToList())
            RemoveNowPlayingMessageAsync(guildId, false).GetAwaiter().GetResult();
        _activeNowPlayingMessages.Clear();

        _musicService.PlayerStopped -= OnPlayerStoppedAsync;
        _musicService.QueueEmptied -= OnQueueEmptiedAsync;

        GC.SuppressFinalize(this);
    }

    private async Task OnPlayerStoppedAsync(ulong guildId)
    {
        _logger.LogDebug("[NP Service] Player stopped for guild {GuildId}, ensuring NP message is removed.", guildId);
        await RemoveNowPlayingMessageAsync(guildId).ConfigureAwait(false);
    }

    private async Task OnQueueEmptiedAsync(ulong guildId, CustomPlayer player)
    {
        // If player is still connected but queue is empty AND not playing, remove NP.
        // Player might be paused on last song or stopped.
        if (player.State == PlayerState.NotPlaying ||
            (player.State == PlayerState.Paused && player.CurrentTrack == null))
        {
            _logger.LogDebug(
                "[NP Service] Queue emptied and player not actively playing for guild {GuildId}, ensuring NP message is removed.",
                guildId);
            await RemoveNowPlayingMessageAsync(guildId).ConfigureAwait(false);
        }
    }

    public async Task<IUserMessage?> CreateOrReplaceNowPlayingMessageAsync(CustomPlayer player,
        SocketInteractionContext interactionContext)
    {
        if (interactionContext.Channel is ITextChannel textChannel)
            return await CreateOrReplaceNowPlayingMessageInternalAsync(player, textChannel, interactionContext.User)
                .ConfigureAwait(false);
        _logger.LogWarning("Interaction context channel is not ITextChannel for NP message. Guild: {GuildId}",
            interactionContext.Guild.Id);
        return null;
    }

    public async Task<IUserMessage?> CreateOrReplaceNowPlayingMessageAsync(CustomPlayer player, ITextChannel channel,
        IUser requester) => await CreateOrReplaceNowPlayingMessageInternalAsync(player, channel, requester)
        .ConfigureAwait(false);

    private async Task<IUserMessage?> CreateOrReplaceNowPlayingMessageInternalAsync(CustomPlayer player,
        ITextChannel channel, IUser requester)
    {
        var guildId = player.GuildId;
        await RemoveNowPlayingMessageAsync(guildId).ConfigureAwait(false);

        var components = MusicUiFactory.BuildNowPlayingDisplay(player, guildId, _config);
        IUserMessage? sentMessage;

        try
        {
            sentMessage = await channel.SendMessageAsync(components: components, flags: MessageFlags.ComponentsV2)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Now Playing message for Guild {GuildId}", guildId);
            return null;
        }

        if (sentMessage == null)
        {
            _logger.LogWarning("Sent Now Playing message was null for Guild {GuildId}", guildId);
            return null;
        }

        var cts = new CancellationTokenSource();
        var npInfo = new NowPlayingMessageInfo(sentMessage.Id, sentMessage, requester.Id, cts, channel.Id);

        if (_activeNowPlayingMessages.TryAdd(guildId, npInfo))
        {
            _ = Task.Run(() => GuildNowPlayingUpdateLoopAsync(guildId, cts.Token), cts.Token);
            _logger.LogInformation(
                "Created Now Playing message {MessageId} for Guild {GuildId} in Channel {ChannelId}. Requested by {RequesterId}",
                sentMessage.Id, guildId, channel.Id, requester.Id);
        }
        else
        {
            _logger.LogWarning("Failed to add Now Playing info for Guild {GuildId} due to concurrent modification.",
                guildId);
            await cts.CancelAsync();
            cts.Dispose();
            try
            {
                await sentMessage.DeleteAsync().ConfigureAwait(false);
            }
            catch
            {
                /* ignored */
            }

            return null;
        }

        return sentMessage;
    }

    public async Task RemoveNowPlayingMessageAsync(ulong guildId, bool deleteDiscordMessage = true)
    {
        if (_activeNowPlayingMessages.TryRemove(guildId, out var info))
        {
            if (!info.UpdateTaskCts.IsCancellationRequested) await info.UpdateTaskCts.CancelAsync();
            info.UpdateTaskCts.Dispose();

            switch (deleteDiscordMessage)
            {
                case true when info.MessageInstance != null:
                    try
                    {
                        await info.MessageInstance.DeleteAsync().ConfigureAwait(false);
                        _logger.LogInformation("Deleted Now Playing message {MessageId} for Guild {GuildId}.",
                            info.MessageId, guildId);
                    }
                    catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.NotFound)
                    {
                        _logger.LogWarning("Now Playing message {MessageId} for Guild {GuildId} was already deleted.",
                            info.MessageId, guildId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to delete Now Playing message {MessageId} for Guild {GuildId}.",
                            info.MessageId, guildId);
                    }

                    break;
                case true when info.MessageInstance == null:
                {
                    if (_client.GetChannel(info.TextChannelId) is ITextChannel textChannel)
                        try
                        {
                            var msg = await textChannel.GetMessageAsync(info.MessageId).ConfigureAwait(false);
                            if (msg != null) await msg.DeleteAsync().ConfigureAwait(false);
                            _logger.LogInformation(
                                "Fetched and deleted Now Playing message {MessageId} for Guild {GuildId}.",
                                info.MessageId,
                                guildId);
                        }
                        catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.NotFound)
                        {
                            _logger.LogWarning(
                                "Now Playing message {MessageId} (fetched by ID) for Guild {GuildId} was already deleted.",
                                info.MessageId, guildId);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex,
                                "Failed to fetch and delete Now Playing message {MessageId} for Guild {GuildId}.",
                                info.MessageId, guildId);
                        }

                    break;
                }
            }

            _logger.LogDebug("Removed Now Playing message info for Guild {GuildId}.", guildId);
        }
    }

    public async Task UpdateNowPlayingMessageAsync(ulong guildId, CustomPlayer? playerInstance = null)
    {
        if (!_activeNowPlayingMessages.TryGetValue(guildId, out var npInfo) || npInfo.MessageInstance == null)
        {
            if (npInfo is { MessageInstance: null })
            {
                // Info exists but message instance is gone
                _logger.LogDebug("NP Message instance for guild {GuildId} is null, attempting to re-fetch or remove.",
                    guildId);
                if (_client.GetChannel(npInfo.TextChannelId) is ITextChannel textChannel)
                {
                    try
                    {
                        if (await textChannel.GetMessageAsync(npInfo.MessageId).ConfigureAwait(false) is IUserMessage
                            fetchedMsg)
                        {
                            _activeNowPlayingMessages.TryUpdate(guildId,
                                npInfo with { MessageInstance = fetchedMsg },
                                npInfo); // Update with fetched instance
                            npInfo = npInfo with { MessageInstance = fetchedMsg }; // for current scope
                        }
                        else
                        {
                            await RemoveNowPlayingMessageAsync(guildId, false).ConfigureAwait(false);
                            return;
                        }
                    }
                    catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.NotFound)
                    {
                        await RemoveNowPlayingMessageAsync(guildId, false).ConfigureAwait(false);
                        return;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error re-fetching NP message for guild {guildId}", guildId);
                        // Potentially remove to stop trying, or just skip this update
                        return;
                    }
                }
                else
                {
                    await RemoveNowPlayingMessageAsync(guildId, false)
                        .ConfigureAwait(false); // Channel no longer accessible
                    return;
                }
            }
            else
            {
                return;
            }
        }

        var player = playerInstance;
        if (player == null)
            if (_client.GetGuild(guildId)?.GetTextChannel(npInfo.TextChannelId) is ITextChannel tc)
            {
                var (retrievedPlayer, _) = await _musicService.GetPlayerAsync(guildId, null, tc,
                    PlayerChannelBehavior.None, MemberVoiceStateBehavior.Ignore).ConfigureAwait(false);
                player = retrievedPlayer;
            }

        if (player == null || player.CurrentTrack == null || player.State == PlayerState.Destroyed)
        {
            await RemoveNowPlayingMessageAsync(guildId, player == null || player.CurrentTrack == null)
                .ConfigureAwait(false);
            return;
        }

        try
        {
            var components = MusicUiFactory.BuildNowPlayingDisplay(player, guildId, _config);
            await npInfo.MessageInstance!.ModifyAsync(props => { props.Components = components; })
                .ConfigureAwait(false);
        }
        catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Now Playing message {MessageId} for Guild {GuildId} not found during update. Removing.",
                npInfo.MessageId, guildId);
            await RemoveNowPlayingMessageAsync(guildId, false).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating Now Playing message {MessageId} for Guild {GuildId}.",
                npInfo.MessageId, guildId);
        }
    }

    private async Task GuildNowPlayingUpdateLoopAsync(ulong guildId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Starting NP update loop for Guild {GuildId}", guildId);
        while (!cancellationToken.IsCancellationRequested)
            try
            {
                await Task.Delay(_updateInterval, cancellationToken).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested) break;

                await UpdateNowPlayingMessageAsync(guildId).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in GuildNowPlayingUpdateLoopAsync for Guild {GuildId}",
                    guildId);
            }

        _logger.LogTrace("Exited NP update loop for Guild {GuildId}", guildId);
    }

    // State
    private record NowPlayingMessageInfo(
        ulong MessageId,
        IUserMessage? MessageInstance,
        ulong RequesterId,
        CancellationTokenSource UpdateTaskCts,
        ulong TextChannelId);
}