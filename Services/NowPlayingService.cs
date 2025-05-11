using System.Collections.Concurrent;
using System.Net;
using Assistant.Net.Configuration;
using Assistant.Net.Modules.Music.Player;
using Assistant.Net.Utilities;
using Discord;
using Discord.Interactions;
using Discord.Net;
using Discord.WebSocket;
using Lavalink4NET.Clients;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Services;

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
        await RemoveNowPlayingMessageAsync(guildId);
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
            await RemoveNowPlayingMessageAsync(guildId);
        }
    }

    public async Task<IUserMessage?> CreateOrReplaceNowPlayingMessageAsync(CustomPlayer player,
        SocketInteractionContext interactionContext)
    {
        if (interactionContext.Channel is ITextChannel textChannel)
            return await CreateOrReplaceNowPlayingMessageInternalAsync(player, textChannel, interactionContext.User);
        _logger.LogWarning("Interaction context channel is not ITextChannel for NP message. Guild: {GuildId}",
            interactionContext.Guild.Id);
        return null;
    }

    public async Task<IUserMessage?> CreateOrReplaceNowPlayingMessageAsync(CustomPlayer player, ITextChannel channel,
        IUser requester) => await CreateOrReplaceNowPlayingMessageInternalAsync(player, channel, requester);

    private async Task<IUserMessage?> CreateOrReplaceNowPlayingMessageInternalAsync(CustomPlayer player,
        ITextChannel channel, IUser requester)
    {
        var guildId = player.GuildId;
        await RemoveNowPlayingMessageAsync(guildId, true);

        var (embed, components) = BuildNowPlayingDisplay(player, guildId);
        IUserMessage? sentMessage;

        try
        {
            sentMessage = await channel.SendMessageAsync(embed: embed, components: components);
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
            cts.Cancel();
            cts.Dispose();
            try
            {
                await sentMessage.DeleteAsync();
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
            if (!info.UpdateTaskCts.IsCancellationRequested) info.UpdateTaskCts.Cancel();
            info.UpdateTaskCts.Dispose();

            if (deleteDiscordMessage && info.MessageInstance != null)
                try
                {
                    await info.MessageInstance.DeleteAsync();
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
            else if (deleteDiscordMessage && info.MessageInstance == null)
                if (_client.GetChannel(info.TextChannelId) is ITextChannel textChannel)
                    try
                    {
                        var msg = await textChannel.GetMessageAsync(info.MessageId);
                        if (msg != null) await msg.DeleteAsync();
                        _logger.LogInformation(
                            "Fetched and deleted Now Playing message {MessageId} for Guild {GuildId}.", info.MessageId,
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

            _logger.LogDebug("Removed Now Playing message info for Guild {GuildId}.", guildId);
        }
    }

    public async Task UpdateNowPlayingMessageAsync(ulong guildId, CustomPlayer? playerInstance = null)
    {
        if (!_activeNowPlayingMessages.TryGetValue(guildId, out var npInfo) || npInfo.MessageInstance == null)
        {
            if (npInfo != null && npInfo.MessageInstance == null)
            {
                // Info exists but message instance is gone
                _logger.LogDebug("NP Message instance for guild {GuildId} is null, attempting to re-fetch or remove.",
                    guildId);
                if (_client.GetChannel(npInfo.TextChannelId) is ITextChannel textChannel)
                {
                    try
                    {
                        if (await textChannel.GetMessageAsync(npInfo.MessageId) is IUserMessage fetchedMsg)
                        {
                            _activeNowPlayingMessages.TryUpdate(guildId,
                                npInfo with { MessageInstance = fetchedMsg },
                                npInfo); // Update with fetched instance
                            npInfo = npInfo with { MessageInstance = fetchedMsg }; // for current scope
                        }
                        else
                        {
                            await RemoveNowPlayingMessageAsync(guildId, false);
                            return;
                        }
                    }
                    catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.NotFound)
                    {
                        await RemoveNowPlayingMessageAsync(guildId, false);
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
                    await RemoveNowPlayingMessageAsync(guildId, false); // Channel no longer accessible
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
                    PlayerChannelBehavior.None, MemberVoiceStateBehavior.Ignore);
                player = retrievedPlayer;
            }

        if (player == null || player.CurrentTrack == null || player.State == PlayerState.Destroyed)
        {
            await RemoveNowPlayingMessageAsync(guildId, player == null || player.CurrentTrack == null);
            return;
        }

        try
        {
            var (embed, components) = BuildNowPlayingDisplay(player, guildId);
            await npInfo.MessageInstance!.ModifyAsync(props =>
            {
                props.Embed = embed;
                props.Components = components;
            });
        }
        catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Now Playing message {MessageId} for Guild {GuildId} not found during update. Removing.",
                npInfo.MessageId, guildId);
            await RemoveNowPlayingMessageAsync(guildId, false);
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
                await Task.Delay(_updateInterval, cancellationToken);
                if (cancellationToken.IsCancellationRequested) break;

                await UpdateNowPlayingMessageAsync(guildId);
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

        _logger.LogDebug("Exited NP update loop for Guild {GuildId}", guildId);
    }

    public (Embed Embed, MessageComponent Components) BuildNowPlayingDisplay(CustomPlayer player, ulong guildId)
    {
        var embedBuilder = new EmbedBuilder().WithColor(0x1ED760); // Spotify Green

        var currentTrack = player.CurrentTrack;
        var queue = player.Queue;

        if (currentTrack != null)
        {
            embedBuilder.WithAuthor(currentTrack.Title, url: currentTrack.Uri?.ToString(),
                iconUrl: currentTrack.ArtworkUri?.ToString());
            if (currentTrack.ArtworkUri != null) embedBuilder.WithThumbnailUrl(currentTrack.ArtworkUri.ToString());
            if (player.Position?.Position != null)
            {
                var position = player.Position.Value.Position;
                var progressBar = MusicUtils.CreateProgressBar(position, currentTrack.Duration);
                var currentTime = TimeUtils.FormatPlayerTime(position);
                var totalTime = TimeUtils.FormatPlayerTime(currentTrack.Duration);
                embedBuilder.AddField($"{currentTime} {progressBar} {totalTime}", "\u200B", false);
            }
        }
        else
        {
            embedBuilder.WithTitle("No song currently playing");
            embedBuilder.WithDescription($"Use `/play` to add songs. {_config.Client.Prefix}play also works.");
        }

        if (!queue.IsEmpty)
        {
            var nextTrack = queue[0].Track;
            if (nextTrack != null)
                embedBuilder.WithFooter($"Next: {nextTrack.Title.Truncate(50)} | {queue.Count} in queue",
                    nextTrack.ArtworkUri?.ToString());
            else
                embedBuilder.WithFooter($"{queue.Count} songs in queue");
        }
        else
        {
            embedBuilder.WithFooter("Queue is empty");
        }

        if (player.RepeatMode == TrackRepeatMode.Track)
            embedBuilder.Footer.Text += " | 🔂 Looping Track";
        else if (player.RepeatMode == TrackRepeatMode.Queue) embedBuilder.Footer.Text += " | 🔁 Looping Queue";


        // Components
        var cb = new ComponentBuilder();
        var controlsDisabled = currentTrack == null;
        var volumeControlsDisabled = currentTrack == null; // Volume can sometimes be set when paused

        // Row 0: Playback Controls
        cb.WithButton(null, $"{NpCustomIdPrefix}:{guildId}:prev_restart", ButtonStyle.Primary, Emoji.Parse("⏮️"),
            disabled: controlsDisabled, row: 0);
        cb.WithButton(null, $"{NpCustomIdPrefix}:{guildId}:rewind", ButtonStyle.Primary, Emoji.Parse("⏪"),
            disabled: controlsDisabled, row: 0);
        cb.WithButton(null, $"{NpCustomIdPrefix}:{guildId}:pause_resume",
            player.State == PlayerState.Paused ? ButtonStyle.Success : ButtonStyle.Primary,
            player.State == PlayerState.Paused ? Emoji.Parse("▶️") : Emoji.Parse("⏸️"), disabled: controlsDisabled,
            row: 0);
        cb.WithButton(null, $"{NpCustomIdPrefix}:{guildId}:forward", ButtonStyle.Primary, Emoji.Parse("⏩"),
            disabled: controlsDisabled, row: 0);
        cb.WithButton(null, $"{NpCustomIdPrefix}:{guildId}:skip", ButtonStyle.Primary, Emoji.Parse("⏭️"),
            disabled: controlsDisabled, row: 0);

        // Row 1: Player/Queue Controls
        cb.WithButton("Stop", $"{NpCustomIdPrefix}:{guildId}:stop", ButtonStyle.Danger, Emoji.Parse("⏹️"),
            disabled: controlsDisabled, row: 1);

        var loopEmoji = player.RepeatMode switch
        {
            TrackRepeatMode.Track => Emoji.Parse("🔂"), // Repeat One
            TrackRepeatMode.Queue => Emoji.Parse("🔁"), // Repeat All
            _ => Emoji.Parse("➡️") // Loop off / Next
        };
        cb.WithButton("Loop", $"{NpCustomIdPrefix}:{guildId}:loop", ButtonStyle.Secondary, loopEmoji,
            disabled: controlsDisabled, row: 1);


        // Row 2: Volume Controls
        var currentVolumePercent = (int)(player.Volume * 100);
        var maxVolume = _config.Music.MaxPlayerVolumePercent;

        cb.WithButton(null, $"{NpCustomIdPrefix}:{guildId}:vol_down", ButtonStyle.Success, Emoji.Parse("➖"),
            disabled: volumeControlsDisabled || currentVolumePercent <= 0, row: 2);
        cb.WithButton($"🔊 {currentVolumePercent}%", $"{NpCustomIdPrefix}:{guildId}:vol_display", ButtonStyle.Secondary,
            disabled: true, row: 2);
        cb.WithButton(null, $"{NpCustomIdPrefix}:{guildId}:vol_up", ButtonStyle.Success, Emoji.Parse("➕"),
            disabled: volumeControlsDisabled || currentVolumePercent >= maxVolume, row: 2);

        return (embedBuilder.Build(), cb.Build());
    }

    // State
    private record NowPlayingMessageInfo(
        ulong MessageId,
        IUserMessage? MessageInstance,
        ulong RequesterId,
        CancellationTokenSource UpdateTaskCts,
        ulong TextChannelId);
}