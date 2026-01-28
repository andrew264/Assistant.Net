using Assistant.Net.Models.Music;
using Assistant.Net.Options;
using Assistant.Net.Services.Data;
using Assistant.Net.Services.Music.Logic;
using Assistant.Net.Utilities;
using Assistant.Net.Utilities.Ui;
using Discord;
using Discord.WebSocket;
using Lavalink4NET;
using Lavalink4NET.Clients;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.Rest;
using Lavalink4NET.Rest.Entities.Tracks;
using Lavalink4NET.Tracks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Net.Services.Music;

public class MusicService(
    IAudioService audioService,
    MusicHistoryService musicHistoryService,
    IOptions<DiscordOptions> options,
    IOptions<MusicOptions> musicOptions,
    DiscordSocketClient client,
    ILogger<MusicService> logger)
{
    private const int MaxRetryAttempts = 2;
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromMilliseconds(500);

    public event Func<ulong, Task>? PlayerStopped;
    public event Func<ulong, CustomPlayer, Task>? QueueEmptied;

    public async ValueTask<(CustomPlayer? Player, PlayerRetrieveStatus Status)> GetPlayerAsync(
        ulong guildId,
        ulong? userVoiceChannelId,
        ITextChannel targetTextChannel,
        PlayerChannelBehavior channelBehavior,
        MemberVoiceStateBehavior memberBehavior)
    {
        if (channelBehavior == PlayerChannelBehavior.Join && userVoiceChannelId is null)
        {
            logger.LogDebug(
                "GetPlayerAsync: Attempted to join voice channel but userVoiceChannelId is null for Guild {GuildId}.",
                guildId);
            return (null, PlayerRetrieveStatus.UserNotInVoiceChannel);
        }

        var playerOptions = new CustomPlayerOptions
        {
            TextChannel = targetTextChannel,
            SocketClient = client,
            DiscordOptions = options.Value,
            MusicOptions = musicOptions.Value,
            InitialVolume = await musicHistoryService.GetGuildVolumeAsync(guildId).ConfigureAwait(false)
        };

        var retrieveOptions = new PlayerRetrieveOptions(channelBehavior, memberBehavior);

        for (var attempt = 0; attempt < MaxRetryAttempts; attempt++)
            try
            {
                var result = await audioService.Players.RetrieveAsync<CustomPlayer, CustomPlayerOptions>(
                    guildId,
                    userVoiceChannelId,
                    static (props, ct) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        return ValueTask.FromResult(new CustomPlayer(props));
                    },
                    playerOptions,
                    retrieveOptions).ConfigureAwait(false);

                if (!result.IsSuccess)
                {
                    logger.LogWarning(
                        "Failed to retrieve player for Guild {GuildId} on attempt {Attempt}/{MaxAttempts}. Status: {Status}. VoiceChannelId: {VoiceChannelId}",
                        guildId, attempt + 1, MaxRetryAttempts, result.Status, userVoiceChannelId);

                    if (attempt >= MaxRetryAttempts - 1) return (result.Player, result.Status);
                    var delay = InitialRetryDelay * Math.Pow(2, attempt);
                    logger.LogWarning(
                        "Retrying player retrieval for Guild {GuildId} after {Delay}ms (attempt {NextAttempt}/{MaxAttempts})",
                        guildId, delay.TotalMilliseconds, attempt + 2, MaxRetryAttempts);
                    await Task.Delay(delay).ConfigureAwait(false);
                    continue;
                }

                if (result.Player != null)
                    logger.LogTrace(
                        "Successfully retrieved player for Guild {GuildId} on attempt {Attempt}/{MaxAttempts}. Status: {Status}.",
                        guildId, attempt + 1, MaxRetryAttempts, result.Status);

                return (result.Player, result.Status);
            }
            catch (TimeoutException ex)
            {
                logger.LogWarning(ex,
                    "TimeoutException retrieving player for Guild {GuildId} on attempt {Attempt}/{MaxAttempts}. VoiceChannelId: {VoiceChannelId}",
                    guildId, attempt + 1, MaxRetryAttempts, userVoiceChannelId);

                if (attempt >= MaxRetryAttempts - 1)
                {
                    logger.LogError(
                        "All {MaxAttempts} attempts to retrieve player for Guild {GuildId} failed due to timeout.",
                        MaxRetryAttempts, guildId);
                    return (null, PlayerRetrieveStatus.PreconditionFailed);
                }

                var delay = InitialRetryDelay * Math.Pow(2, attempt);
                logger.LogWarning(
                    "Retrying player retrieval for Guild {GuildId} after {Delay}ms due to timeout (attempt {NextAttempt}/{MaxAttempts})",
                    guildId, delay.TotalMilliseconds, attempt + 2, MaxRetryAttempts);
                await Task.Delay(delay).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Unexpected exception retrieving player for Guild {GuildId} on attempt {Attempt}/{MaxAttempts}",
                    guildId, attempt + 1, MaxRetryAttempts);

                return (null, PlayerRetrieveStatus.PreconditionFailed);
            }

        logger.LogError("Unexpectedly exhausted all retry attempts for Guild {GuildId}", guildId);
        return (null, PlayerRetrieveStatus.PreconditionFailed);
    }


    public async ValueTask<(CustomPlayer? Player, PlayerRetrieveStatus Status)> GetPlayerForContextAsync(
        IGuild guild,
        IUser user,
        IMessageChannel channel,
        PlayerChannelBehavior channelBehavior,
        MemberVoiceStateBehavior memberBehavior)
    {
        if (user is not IGuildUser guildUser)
        {
            logger.LogError("GetPlayerForContextAsync called by user {UserId} who is not a guild user in this context.",
                user.Id);
            return (null, PlayerRetrieveStatus.UserNotInVoiceChannel);
        }

        if (channel is ITextChannel textChannel)
            return await GetPlayerAsync(
                guild.Id,
                guildUser.VoiceChannel?.Id,
                textChannel,
                channelBehavior,
                memberBehavior).ConfigureAwait(false);

        logger.LogError("GetPlayerForContextAsync called from non-text channel {ChannelId} ({ChannelName})",
            channel.Id, channel.Name);
        return (null, PlayerRetrieveStatus.UserNotInVoiceChannel);
    }


    public async Task StartPlaybackIfNeededAsync(CustomPlayer player)
    {
        if (player.State is PlayerState.NotPlaying or PlayerState.Destroyed)
        {
            var nextTrack = await player.Queue.TryDequeueAsync().ConfigureAwait(false);
            if (nextTrack?.Track is not null)
            {
                logger.LogDebug("[MusicService:{GuildId}] Starting playback with track: {TrackTitle}",
                    player.GuildId, nextTrack.Track.Title);
                await player.PlayAsync(nextTrack, false).ConfigureAwait(false);
            }
            else
            {
                logger.LogDebug(
                    "[MusicService:{GuildId}] Tried to start playback, but queue is empty or dequeued track is null.",
                    player.GuildId);

                if (player.Queue.IsEmpty && player.State == PlayerState.NotPlaying && QueueEmptied != null)
                    await QueueEmptied.Invoke(player.GuildId, player).ConfigureAwait(false);
            }
        }
    }

    public async Task<TrackLoadResultInfo> LoadAndQueueTrackAsync(CustomPlayer player, string query, IUser requester)
    {
        logger.LogDebug("[MusicService:{GuildId}] Received play request by {User} with query: {Query}",
            player.GuildId, requester.Username, query);

        var isUrl = Uri.TryCreate(query, UriKind.Absolute, out _);
        var searchMode = isUrl ? TrackSearchMode.None : TrackSearchMode.YouTube;
        TrackLoadResult lavalinkResult;
        var resolutionScope = new LavalinkApiResolutionScope(player.ApiClient);

        try
        {
            lavalinkResult = await audioService.Tracks.LoadTracksAsync(query, searchMode, resolutionScope)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[MusicService:{GuildId}] Exception during LoadTracksAsync for query '{Query}'",
                player.GuildId, query);
            return TrackLoadResultInfo.FromError($"An error occurred while searching: {ex.Message}", query);
        }

        if (lavalinkResult.IsPlaylist)
        {
            var playlist = lavalinkResult.Playlist!;
            var tracks = lavalinkResult.Tracks;
            if (tracks.IsEmpty)
                return TrackLoadResultInfo.FromError($"Playlist '{playlist.Name}' is empty or could not be loaded.",
                    query);

            var trackItems = tracks.Select(t => new CustomTrackQueueItem(t, requester.Id)).ToList();
            await player.Queue.AddRangeAsync(trackItems).ConfigureAwait(false);
            return TrackLoadResultInfo.FromPlaylist(tracks, playlist, query);
        }

        if (searchMode != TrackSearchMode.None && !lavalinkResult.Tracks.IsEmpty)

            return TrackLoadResultInfo.FromSearchResults(lavalinkResult.Tracks, query);

        if (lavalinkResult.Track is not null)
        {
            await player.Queue.AddAsync(new CustomTrackQueueItem(lavalinkResult.Track, requester.Id))
                .ConfigureAwait(false);
            return TrackLoadResultInfo.FromSuccess(lavalinkResult.Track, query);
        }

        if (lavalinkResult.Exception is null) return TrackLoadResultInfo.FromNoMatches(query);
        logger.LogError(
            "[MusicService:{GuildId}] Track loading failed for query '{Query}'. Reason: {Reason}, Severity: {Severity}, Cause: {Cause}",
            player.GuildId, query, lavalinkResult.Exception?.Message, lavalinkResult.Exception?.Severity,
            lavalinkResult.Exception?.Cause);
        return TrackLoadResultInfo.FromError(lavalinkResult.Exception?.Message ?? "Unknown error", query);
    }

    public async Task<LavalinkTrack?> GetTrackFromSearchSelectionAsync(CustomPlayer player, string uri)
    {
        var resolutionScope = new LavalinkApiResolutionScope(player.ApiClient);
        try
        {
            var result = await audioService.Tracks.LoadTracksAsync(uri, TrackSearchMode.None, resolutionScope)
                .ConfigureAwait(false);
            return result.Track;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[MusicService] Failed to load selected track from search: {Uri}", uri);
            return null;
        }
    }


    public async Task<(bool Success, LavalinkTrack? SkippedTrack, string Message)> SkipTrackAsync(CustomPlayer player,
        IUser requester, int index = 0)
    {
        if (player.CurrentTrack is null) return (false, null, "I am not playing anything right now.");

        if (index < 0) index = player.Queue.Count + index + 1;

        LavalinkTrack? trackToReport;
        string actionMessage;

        switch (index)
        {
            case 0:
                trackToReport = player.CurrentTrack;
                await player.SkipAsync().ConfigureAwait(false);
                actionMessage = $"Skipping {trackToReport.Title.AsMarkdownLink(trackToReport.Uri?.ToString())}";
                logger.LogDebug("[MusicService:{GuildId}] Skipped current track '{TrackTitle}' by {User}",
                    player.GuildId, trackToReport.Title, requester.Username);
                break;

            case > 0 when index <= player.Queue.Count:
            {
                var queuedTrackItem = player.Queue[index - 1];
                if (queuedTrackItem.Track is null)
                    return (false, null, "Invalid track at the specified index in the queue.");
                trackToReport = queuedTrackItem.Track;
                await player.Queue.RemoveAtAsync(index - 1).ConfigureAwait(false);
                actionMessage =
                    $"Removed from queue: {trackToReport.Title.AsMarkdownLink(trackToReport.Uri?.ToString())}";
                logger.LogDebug(
                    "[MusicService:{GuildId}] Removed track '{TrackTitle}' from queue at index {Index} by {User}",
                    player.GuildId, trackToReport.Title, index, requester.Username);

                if (player.Queue.IsEmpty && player.CurrentTrack == null && QueueEmptied != null)
                    await QueueEmptied.Invoke(player.GuildId, player).ConfigureAwait(false);
                break;
            }
            default:
                return (false, null, "Invalid index. The queue is not that long.");
        }

        return (true, trackToReport, actionMessage);
    }

    public string ToggleLoopMode(CustomPlayer player, IUser requester)
    {
        if (player.CurrentTrack is null) return "I am not playing anything right now.";

        string replyMessage;
        switch (player.RepeatMode)
        {
            case TrackRepeatMode.None:
                player.RepeatMode = TrackRepeatMode.Track;
                replyMessage =
                    $"Looping {player.CurrentTrack.Title.AsMarkdownLink(player.CurrentTrack.Uri?.ToString())}.";
                break;
            case TrackRepeatMode.Track:
                player.RepeatMode = TrackRepeatMode.Queue;
                replyMessage = $"Looping all {player.Queue.Count + (player.CurrentTrack != null ? 1 : 0)} tracks.";
                break;
            case TrackRepeatMode.Queue:
                player.RepeatMode = TrackRepeatMode.None;
                replyMessage = "Stopped looping.";
                break;
            default:
                replyMessage = "An unexpected error occurred with the loop mode.";
                logger.LogError("[MusicService:{GuildId}] Unknown RepeatMode: {RepeatMode}", player.GuildId,
                    player.RepeatMode);
                break;
        }

        logger.LogDebug("[MusicService:{GuildId}] Loop mode set to {RepeatMode} by {User}", player.GuildId,
            player.RepeatMode, requester.Username);
        return replyMessage;
    }

    public async Task<(bool Success, string Message)> SetVolumeAsync(CustomPlayer player, IUser requester,
        int volumePercentage)
    {
        var maxVolume = musicOptions.Value.MaxPlayerVolumePercent;
        if (volumePercentage < 0 || volumePercentage > maxVolume)
            return (false, $"Volume out of range. Please use a value between 0 and {maxVolume}.");

        var volumeFloat = volumePercentage / 100f;
        await player.SetVolumeAsync(volumeFloat).ConfigureAwait(false);
        await musicHistoryService.SetGuildVolumeAsync(player.GuildId, volumeFloat).ConfigureAwait(false);
        logger.LogDebug("[MusicService:{GuildId}] Volume set to {Volume}% by {User}", player.GuildId,
            volumePercentage, requester.Username);
        return (true, $"Volume set to `{volumePercentage}%`");
    }

    public static float GetCurrentVolumePercent(CustomPlayer player) => player.Volume * 100f;


    public async Task StopPlaybackAsync(CustomPlayer player, IUser requester)
    {
        var guildId = player.GuildId;
        await player.Queue.ClearAsync().ConfigureAwait(false);
        await player.StopAsync().ConfigureAwait(false);
        await player.DisconnectAsync().ConfigureAwait(false);
        logger.LogDebug("[MusicService:{GuildId}] Stopped and cleared queue by {User}", guildId,
            requester.Username);

        PlayerStopped?.Invoke(guildId);
    }

    public async Task<(bool Success, string Message)> SeekAsync(CustomPlayer player, IUser requester, string timeInput)
    {
        if (player.CurrentTrack is null) return (false, "I am not playing anything right now.");

        var timeSpan = TimeUtils.ParseTimestamp(timeInput);
        if (timeSpan == TimeSpan.Zero && !string.IsNullOrWhiteSpace(timeInput) && timeInput != "0" &&
            timeInput != "0:0" &&
            timeInput != "0:0:0") return (false, "Invalid time format. Please use `HH:MM:SS`, `MM:SS`, or `SS`.");

        if (timeSpan > player.CurrentTrack.Duration)
            return (false, $"Cannot seek beyond the song's duration ({player.CurrentTrack.Duration:mm\\:ss}).");
        if (timeSpan < TimeSpan.Zero) return (false, "Cannot seek to a negative time.");

        await player.SeekAsync(timeSpan).ConfigureAwait(false);
        logger.LogDebug("[MusicService:{GuildId}] Seeked track '{TrackTitle}' to {Time} by {User}",
            player.GuildId, player.CurrentTrack.Title, timeSpan, requester.Username);
        return (true,
            $"Seeked {player.CurrentTrack.Title.AsMarkdownLink(player.CurrentTrack.Uri?.ToString())} to `{timeSpan:mm\\:ss}`.");
    }

    public async Task<(bool Success, string Message)> SkipToTrackAsync(CustomPlayer player, IUser requester, int index)
    {
        if (player.CurrentTrack is null) return (false, "I am not playing anything right now.");

        if (index < 0) index = player.Queue.Count + index + 1;

        var currentTrack = player.CurrentTrack;

        if (index == 0)
        {
            await player.SeekAsync(TimeSpan.Zero).ConfigureAwait(false);
            logger.LogDebug("[MusicService:{GuildId}] Restarted track '{TrackTitle}' by {User}",
                player.GuildId, currentTrack.Title, requester.Username);
            return (true, $"Restarting {currentTrack.Title.AsMarkdownLink(currentTrack.Uri?.ToString())}.");
        }

        if (index > player.Queue.Count || index < 1)
            return (false, "Invalid index. The queue is not that long or index is out of bounds.");

        var trackToPlayItem = player.Queue[index - 1];
        if (trackToPlayItem.Track is null) return (false, "Invalid track data at the specified queue index.");


        for (var i = 0; i < index - 1; i++) await player.Queue.TryDequeueAsync().ConfigureAwait(false);


        await player.SkipAsync().ConfigureAwait(false);
        logger.LogDebug("[MusicService:{GuildId}] Skipped to track '{TrackTitle}' (from index {Index}) by {User}",
            player.GuildId, trackToPlayItem.Track.Title, index, requester.Username);
        return (true,
            $"Skipping to {trackToPlayItem.Track.Title.AsMarkdownLink(trackToPlayItem.Track.Uri?.ToString())}.");
    }

    public async Task<(bool Success, string Message)> PauseOrResumeAsync(CustomPlayer player, IUser requester)
    {
        if (player.CurrentTrack is null) return (false, "I am not playing anything right now.");

        switch (player.State)
        {
            case PlayerState.Playing:
                await player.PauseAsync().ConfigureAwait(false);
                logger.LogDebug("[MusicService:{GuildId}] Paused playback by {User}", player.GuildId,
                    requester.Username);
                return (true,
                    $"Paused: {player.CurrentTrack.Title.AsMarkdownLink(player.CurrentTrack.Uri?.ToString())}");
            case PlayerState.Paused:
                await player.ResumeAsync().ConfigureAwait(false);
                logger.LogDebug("[MusicService:{GuildId}] Resumed playback by {User}", player.GuildId,
                    requester.Username);
                return (true,
                    $"Resumed: {player.CurrentTrack.Title.AsMarkdownLink(player.CurrentTrack.Uri?.ToString())}");
            case PlayerState.Destroyed:
            case PlayerState.NotPlaying:
            default:
                return (false, "Cannot pause or resume in the current state.");
        }
    }

    public static (MessageComponent? Components, string? ErrorMessage) BuildQueueComponents(
        CustomPlayer player,
        int currentPage,
        ulong interactionMessageId,
        ulong requesterId) =>
        MusicUiFactory.BuildQueueComponents(player, currentPage, interactionMessageId, requesterId);

    public async Task<(bool Success, LavalinkTrack? RemovedTrack, string Message)> RemoveFromQueueAsync(
        CustomPlayer player, int oneBasedIndex)
    {
        if (player.Queue.IsEmpty) return (false, null, "The queue is empty.");
        if (oneBasedIndex <= 0 || oneBasedIndex > player.Queue.Count) return (false, null, "Invalid index.");

        var trackToRemove = player.Queue[oneBasedIndex - 1].Track;
        await player.Queue.RemoveAtAsync(oneBasedIndex - 1).ConfigureAwait(false);

        var message = trackToRemove != null
            ? $"Removed {trackToRemove.Title.AsMarkdownLink(trackToRemove.Uri?.ToString())} from the queue."
            : "Removed song from the queue.";

        logger.LogDebug("[MusicService:{GuildId}] Removed track at index {Index} from queue.", player.GuildId,
            oneBasedIndex);

        if (player.Queue.IsEmpty && player.CurrentTrack == null && QueueEmptied != null)
            await QueueEmptied.Invoke(player.GuildId, player).ConfigureAwait(false);
        return (true, trackToRemove, message);
    }

    public async Task<(bool Success, string Message)> ClearQueueAsync(CustomPlayer player)
    {
        if (player.Queue.IsEmpty) return (false, "The queue is already empty.");
        var guildId = player.GuildId;

        await player.Queue.ClearAsync().ConfigureAwait(false);
        logger.LogDebug("[MusicService:{GuildId}] Queue cleared.", guildId);

        if (QueueEmptied != null)
            await QueueEmptied.Invoke(guildId, player).ConfigureAwait(false);
        return (true, "Queue cleared.");
    }

    public async Task<(bool Success, string Message)> ShuffleQueueAsync(CustomPlayer player)
    {
        if (player.Queue.Count < 2) return (false, "Not enough songs in the queue to shuffle.");
        await player.Queue.ShuffleAsync().ConfigureAwait(false);
        logger.LogDebug("[MusicService:{GuildId}] Queue shuffled.", player.GuildId);
        return (true, "Queue shuffled.");
    }

    public async Task<(bool Success, LavalinkTrack? MovedTrack, string Message)> MoveInQueueAsync(CustomPlayer player,
        int fromOneBasedIndex, int toOneBasedIndex)
    {
        if (player.Queue.Count < 2) return (false, null, "Not enough songs in the queue to move.");
        if (fromOneBasedIndex <= 0 || fromOneBasedIndex > player.Queue.Count ||
            toOneBasedIndex <= 0 || toOneBasedIndex > player.Queue.Count)
            return (false, null, "Invalid index(es).");
        if (fromOneBasedIndex == toOneBasedIndex) return (false, null, "Song is already in that position.");

        var trackToMoveItem = player.Queue[fromOneBasedIndex - 1];
        await player.Queue.RemoveAtAsync(fromOneBasedIndex - 1).ConfigureAwait(false);
        await player.Queue.InsertAsync(toOneBasedIndex - 1, trackToMoveItem).ConfigureAwait(false);

        var message = trackToMoveItem.Track != null
            ? $"Moved {trackToMoveItem.Track.Title.AsMarkdownLink(trackToMoveItem.Track.Uri?.ToString())} from position `{fromOneBasedIndex}` to `{toOneBasedIndex}`."
            : $"Moved song from position `{fromOneBasedIndex}` to `{toOneBasedIndex}`.";
        logger.LogDebug("[MusicService:{GuildId}] Moved track from {FromIndex} to {ToIndex} in queue.",
            player.GuildId, fromOneBasedIndex, toOneBasedIndex);
        return (true, trackToMoveItem.Track, message);
    }

    public (TrackRepeatMode NewMode, string Message) ToggleQueueLoop(CustomPlayer player)
    {
        if (player.CurrentTrack is null && player.Queue.IsEmpty)
            return (player.RepeatMode, "Nothing is playing and the queue is empty.");

        string message;
        TrackRepeatMode newMode;

        switch (player.RepeatMode)
        {
            case TrackRepeatMode.Queue:
                newMode = TrackRepeatMode.None;
                message = "Queue loop disabled.";
                break;
            case TrackRepeatMode.None:
            case TrackRepeatMode.Track:
                newMode = TrackRepeatMode.Queue;
                message = "Queue loop enabled.";
                break;
            default:
                newMode = TrackRepeatMode.None;
                message = "Queue loop disabled (unexpected state).";
                logger.LogWarning(
                    "[MusicService:{GuildId}] Unexpected RepeatMode {RepeatMode} when toggling queue loop.",
                    player.GuildId, player.RepeatMode);
                break;
        }

        player.RepeatMode = newMode;

        logger.LogDebug("[MusicService:{GuildId}] Queue loop mode set to {NewMode}.", player.GuildId, newMode);
        return (newMode, message);
    }
}