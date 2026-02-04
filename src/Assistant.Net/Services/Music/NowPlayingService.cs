using System.Collections.Concurrent;
using System.Net;
using Assistant.Net.Options;
using Assistant.Net.Services.Music.Logic;
using Assistant.Net.Utilities.Ui;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Lavalink4NET.Clients;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Net.Services.Music;

public class NowPlayingService : IDisposable
{
    public const string NpCustomIdPrefix = "np";

    private readonly ConcurrentDictionary<ulong, NowPlayingMessageInfo> _activeNowPlayingMessages = new();

    private readonly DiscordSocketClient _client;
    private readonly ILogger<NowPlayingService> _logger;
    private readonly MusicOptions _musicOptions;
    private readonly MusicService _musicService;
    private readonly TimeSpan _updateInterval = TimeSpan.FromSeconds(10);

    public NowPlayingService(
        DiscordSocketClient client,
        MusicService musicService,
        IOptions<MusicOptions> musicOptions,
        ILogger<NowPlayingService> logger)
    {
        _client = client;
        _musicService = musicService;
        _musicOptions = musicOptions.Value;
        _logger = logger;

        _musicService.PlayerStopped += OnPlayerStoppedAsync;
        _musicService.QueueEmptied += OnQueueEmptiedAsync;

        _logger.LogInformation("NowPlayingService initialized.");
    }

    public void Dispose()
    {
        _logger.LogInformation("Disposing NowPlayingService and clearing active messages.");

        foreach (var kvp in _activeNowPlayingMessages)
            try
            {
                if (!kvp.Value.UpdateTaskCts.IsCancellationRequested)
                    kvp.Value.UpdateTaskCts.Cancel();
                kvp.Value.UpdateTaskCts.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing CTS for guild {GuildId}", kvp.Key);
            }

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
        if (player.State == PlayerState.NotPlaying ||
            (player.State == PlayerState.Paused && player.CurrentTrack == null))
        {
            _logger.LogDebug(
                "[NP Service] Queue emptied and player not actively playing for guild {GuildId}, ensuring NP message is removed.",
                guildId);
            await RemoveNowPlayingMessageAsync(guildId).ConfigureAwait(false);
        }
    }

    public async Task<IUserMessage?> CreateOrReplaceNowPlayingMessageAsync(CustomPlayer player, ITextChannel channel)
    {
        var guildId = player.GuildId;
        await RemoveNowPlayingMessageAsync(guildId).ConfigureAwait(false);

        var components = MusicUiFactory.BuildNowPlayingDisplay(player, guildId, _musicOptions.MaxPlayerVolumePercent);
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

        return TrackNowPlayingMessage(sentMessage, guildId);
    }

    public IUserMessage? TrackNowPlayingMessage(IUserMessage message, ulong guildId)
    {
        var cts = new CancellationTokenSource();
        var npInfo = new NowPlayingMessageInfo(message.Id, message, cts, message.Channel.Id);

        if (_activeNowPlayingMessages.TryAdd(guildId, npInfo))
        {
            _ = Task.Run(() => GuildNowPlayingUpdateLoopAsync(guildId, cts.Token), cts.Token);
            _logger.LogInformation(
                "Created Now Playing message {MessageId} for Guild {GuildId} in Channel {ChannelId}.",
                message.Id, guildId, message.Channel.Id);
            return message;
        }

        _logger.LogWarning("Failed to add Now Playing info for Guild {GuildId} due to concurrent modification.",
            guildId);
        _ = cts.CancelAsync();
        cts.Dispose();
        return null;
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
        if (!_activeNowPlayingMessages.TryGetValue(guildId, out var npInfo)) return;

        if (npInfo.MessageInstance == null)
        {
            _logger.LogDebug("NP Message instance for guild {GuildId} is null, attempting to re-fetch or remove.",
                guildId);
            if (_client.GetChannel(npInfo.TextChannelId) is ITextChannel textChannel)
            {
                try
                {
                    if (await textChannel.GetMessageAsync(npInfo.MessageId).ConfigureAwait(false) is IUserMessage
                        fetchedMsg)
                    {
                        npInfo.MessageInstance = fetchedMsg;
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
                    return;
                }
            }
            else
            {
                await RemoveNowPlayingMessageAsync(guildId, false).ConfigureAwait(false);
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

        var currentState = new NowPlayingState(
            player.CurrentTrack.Uri?.ToString(),
            player.State,
            player.Position?.Position.TotalSeconds ?? 0d,
            player.Volume,
            player.RepeatMode,
            player.Queue.Count,
            player.Queue.Count > 0 ? player.Queue[0].Track?.Uri?.ToString() : null
        );

        if (npInfo.LastState != null && npInfo.LastState.Equals(currentState)) return;

        try
        {
            var components =
                MusicUiFactory.BuildNowPlayingDisplay(player, guildId, _musicOptions.MaxPlayerVolumePercent);
            await npInfo.MessageInstance!.ModifyAsync(props => { props.Components = components; })
                .ConfigureAwait(false);

            npInfo.LastState = currentState;
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

    private class NowPlayingMessageInfo(
        ulong messageId,
        IUserMessage? messageInstance,
        CancellationTokenSource updateTaskCts,
        ulong textChannelId)
    {
        public ulong MessageId { get; } = messageId;
        public IUserMessage? MessageInstance { get; set; } = messageInstance;
        public CancellationTokenSource UpdateTaskCts { get; } = updateTaskCts;
        public ulong TextChannelId { get; } = textChannelId;

        public NowPlayingState? LastState { get; set; }
    }

    private record NowPlayingState(
        string? TrackUri,
        PlayerState PlayerState,
        double PositionSeconds,
        float Volume,
        TrackRepeatMode RepeatMode,
        int QueueCount,
        string? NextTrackUri
    );
}