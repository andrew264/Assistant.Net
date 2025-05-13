using System.Text;
using Assistant.Net.Configuration;
using Assistant.Net.Models.Music;
using Assistant.Net.Modules.Music.Player;
using Assistant.Net.Utilities;
using Discord;
using Discord.WebSocket;
using Lavalink4NET;
using Lavalink4NET.Clients;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.Rest.Entities.Tracks;
using Lavalink4NET.Tracks;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Services;

public class MusicService(
    IAudioService audioService,
    MusicHistoryService musicHistoryService,
    Config config,
    DiscordSocketClient client,
    ILogger<MusicService> logger)
{
    private const int QueueItemsPerPage = 4;

    // --- Events for NowPlayingService ---
    public event Func<ulong, Task>? PlayerStopped;
    public event Func<ulong, CustomPlayer, Task>? QueueEmptied;

    public async ValueTask<(CustomPlayer? Player, PlayerRetrieveStatus Status)> GetPlayerAsync(
        ulong guildId,
        ulong? userVoiceChannelId,
        ITextChannel targetTextChannel,
        PlayerChannelBehavior channelBehavior,
        MemberVoiceStateBehavior memberBehavior)
    {
        var playerOptions = new CustomPlayerOptions
        {
            TextChannel = targetTextChannel,
            SocketClient = client,
            ApplicationConfig = config,
            InitialVolume = await musicHistoryService.GetGuildVolumeAsync(guildId)
        };

        var retrieveOptions = new PlayerRetrieveOptions(channelBehavior, memberBehavior);

        var result = await audioService.Players.RetrieveAsync<CustomPlayer, CustomPlayerOptions>(
            guildId,
            userVoiceChannelId,
            static (props, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return ValueTask.FromResult(new CustomPlayer(props));
            },
            playerOptions,
            retrieveOptions);

        if (!result.IsSuccess)
            logger.LogWarning(
                "Failed to retrieve player for Guild {GuildId}. Status: {Status}. VoiceChannelId: {VoiceChannelId}, ChannelBehavior: {ChannelBehavior}, MemberBehavior: {MemberBehavior}",
                guildId, result.Status, userVoiceChannelId, channelBehavior, memberBehavior);
        else if (result.Player != null)
            logger.LogTrace(
                "Successfully retrieved player for Guild {GuildId}. Status: {Status}. VoiceChannelId: {VoiceChannelId}, ChannelBehavior: {ChannelBehavior}, MemberBehavior: {MemberBehavior}",
                guildId, result.Status, userVoiceChannelId, channelBehavior, memberBehavior);

        return (result.Player, result.Status);
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
                memberBehavior);

        logger.LogError("GetPlayerForContextAsync called from non-text channel {ChannelId} ({ChannelName})",
            channel.Id, channel.Name);
        return (null, PlayerRetrieveStatus.UserNotInVoiceChannel); // Or a more appropriate status
    }


    public async Task StartPlaybackIfNeededAsync(CustomPlayer player)
    {
        if (player.State is PlayerState.NotPlaying or PlayerState.Destroyed)
        {
            var nextTrack = await player.Queue.TryDequeueAsync();
            if (nextTrack?.Track is not null)
            {
                logger.LogInformation("[MusicService:{GuildId}] Starting playback with track: {TrackTitle}",
                    player.GuildId, nextTrack.Track.Title);
                await player.PlayAsync(nextTrack, false);
            }
            else
            {
                logger.LogDebug(
                    "[MusicService:{GuildId}] Tried to start playback, but queue is empty or dequeued track is null.",
                    player.GuildId);
                // If queue is truly empty now, and player was not playing.
                if (player.Queue.IsEmpty && player.State == PlayerState.NotPlaying && QueueEmptied != null)
                    await QueueEmptied.Invoke(player.GuildId, player);
            }
        }
    }

    public async Task<TrackLoadResultInfo> LoadAndQueueTrackAsync(CustomPlayer player, string query, IUser requester)
    {
        logger.LogInformation("[MusicService:{GuildId}] Received play request by {User} with query: {Query}",
            player.GuildId, requester.Username, query);

        var isUrl = Uri.TryCreate(query, UriKind.Absolute, out _);
        var searchMode = isUrl ? TrackSearchMode.None : TrackSearchMode.YouTube;
        TrackLoadResult lavalinkResult;

        try
        {
            lavalinkResult = await audioService.Tracks.LoadTracksAsync(query, searchMode);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[MusicService:{GuildId}] Exception during LoadTracksAsync for query '{Query}'",
                player.GuildId, query);
            return TrackLoadResultInfo.FromError($"An error occurred while searching: {ex.Message}", query);
        }

        if (lavalinkResult.IsPlaylist)
        {
            var playlist = lavalinkResult.Playlist!; // Not null if IsPlaylist is true
            var tracks = lavalinkResult.Tracks;
            if (tracks.IsEmpty)
                return TrackLoadResultInfo.FromError($"Playlist '{playlist.Name}' is empty or could not be loaded.",
                    query);

            var trackItems = tracks.Select(t => new TrackQueueItem(t)).ToList();
            await player.Queue.AddRangeAsync(trackItems);
            return TrackLoadResultInfo.FromPlaylist(tracks, playlist, query);
        }

        if (searchMode != TrackSearchMode.None && !lavalinkResult.Tracks.IsEmpty)
            // Return search results for the module to handle UI
            return TrackLoadResultInfo.FromSearchResults(lavalinkResult.Tracks, query);

        if (lavalinkResult.Track is not null)
        {
            await player.Queue.AddAsync(new TrackQueueItem(lavalinkResult.Track));
            return TrackLoadResultInfo.FromSuccess(lavalinkResult.Track, query);
        }

        if (lavalinkResult.Exception is not null)
        {
            logger.LogError(
                "[MusicService:{GuildId}] Track loading failed for query '{Query}'. Reason: {Reason}, Severity: {Severity}, Cause: {Cause}",
                player.GuildId, query, lavalinkResult.Exception?.Message, lavalinkResult.Exception?.Severity,
                lavalinkResult.Exception?.Cause);
            return TrackLoadResultInfo.FromError(lavalinkResult.Exception?.Message ?? "Unknown error", query);
        }

        return TrackLoadResultInfo.FromNoMatches(query);
    }

    public async Task<LavalinkTrack?> GetTrackFromSearchSelectionAsync(string uri)
    {
        try
        {
            var result = await audioService.Tracks.LoadTracksAsync(uri, TrackSearchMode.None);
            return result.Track; // Will be null if not found or error
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

        if (index == 0) // Skip current track
        {
            trackToReport = player.CurrentTrack;
            await player.SkipAsync();
            actionMessage = $"Skipping {trackToReport.Title.AsMarkdownLink(trackToReport.Uri?.ToString())}";
            logger.LogInformation("[MusicService:{GuildId}] Skipped current track '{TrackTitle}' by {User}",
                player.GuildId, trackToReport.Title, requester.Username);
        }
        else if (index > 0 && index <= player.Queue.Count) // Skip a track in the queue
        {
            var queuedTrackItem = player.Queue[index - 1];
            if (queuedTrackItem.Track is null)
                return (false, null, "Invalid track at the specified index in the queue.");
            trackToReport = queuedTrackItem.Track;
            await player.Queue.RemoveAtAsync(index - 1);
            actionMessage = $"Removed from queue: {trackToReport.Title.AsMarkdownLink(trackToReport.Uri?.ToString())}";
            logger.LogInformation(
                "[MusicService:{GuildId}] Removed track '{TrackTitle}' from queue at index {Index} by {User}",
                player.GuildId, trackToReport.Title, index, requester.Username);

            if (player.Queue.IsEmpty && player.CurrentTrack == null && QueueEmptied != null) // Check after removal
                await QueueEmptied.Invoke(player.GuildId, player);
        }
        else
        {
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

        logger.LogInformation("[MusicService:{GuildId}] Loop mode set to {RepeatMode} by {User}", player.GuildId,
            player.RepeatMode, requester.Username);
        return replyMessage;
    }

    public async Task<(bool Success, string Message)> SetVolumeAsync(CustomPlayer player, IUser requester,
        int volumePercentage)
    {
        var maxVolume = config.Music.MaxPlayerVolumePercent;
        if (volumePercentage < 0 || volumePercentage > maxVolume)
            return (false, $"Volume out of range. Please use a value between 0 and {maxVolume}.");

        var volumeFloat = volumePercentage / 100f;
        await player.SetVolumeAsync(volumeFloat);
        await musicHistoryService.SetGuildVolumeAsync(player.GuildId, volumeFloat);
        logger.LogInformation("[MusicService:{GuildId}] Volume set to {Volume}% by {User}", player.GuildId,
            volumePercentage, requester.Username);
        return (true, $"Volume set to `{volumePercentage}%`");
    }

    public float GetCurrentVolumePercent(CustomPlayer player) => player.Volume * 100f;


    public async Task StopPlaybackAsync(CustomPlayer player, IUser requester)
    {
        var guildId = player.GuildId; // Capture before player might be disposed
        await player.Queue.ClearAsync();
        await player.StopAsync();
        await player.DisconnectAsync();
        logger.LogInformation("[MusicService:{GuildId}] Stopped and cleared queue by {User}", guildId,
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

        await player.SeekAsync(timeSpan);
        logger.LogInformation("[MusicService:{GuildId}] Seeked track '{TrackTitle}' to {Time} by {User}",
            player.GuildId, player.CurrentTrack.Title, timeSpan, requester.Username);
        return (true,
            $"Seeked {player.CurrentTrack.Title.AsMarkdownLink(player.CurrentTrack.Uri?.ToString())} to `{timeSpan:mm\\:ss}`.");
    }

    public async Task<(bool Success, string Message)> SkipToTrackAsync(CustomPlayer player, IUser requester, int index)
    {
        if (player.CurrentTrack is null) return (false, "I am not playing anything right now.");

        if (index < 0) index = player.Queue.Count + index + 1;

        var currentTrack = player.CurrentTrack;

        if (index == 0) // Restart current track
        {
            await player.SeekAsync(TimeSpan.Zero);
            logger.LogInformation("[MusicService:{GuildId}] Restarted track '{TrackTitle}' by {User}",
                player.GuildId, currentTrack.Title, requester.Username);
            return (true, $"Restarting {currentTrack.Title.AsMarkdownLink(currentTrack.Uri?.ToString())}.");
        }

        if (index > player.Queue.Count || index < 1)
            return (false, "Invalid index. The queue is not that long or index is out of bounds.");

        var trackToPlayItem = player.Queue[index - 1];
        if (trackToPlayItem.Track is null) return (false, "Invalid track data at the specified queue index.");

        // Remove tracks before the target index
        for (var i = 0; i < index - 1; i++) await player.Queue.TryDequeueAsync();

        // The target track is now at the front. SkipAsync will play it.
        await player.SkipAsync();
        logger.LogInformation("[MusicService:{GuildId}] Skipped to track '{TrackTitle}' (from index {Index}) by {User}",
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
                await player.PauseAsync();
                logger.LogInformation("[MusicService:{GuildId}] Paused playback by {User}", player.GuildId,
                    requester.Username);
                return (true,
                    $"Paused: {player.CurrentTrack.Title.AsMarkdownLink(player.CurrentTrack.Uri?.ToString())}");
            case PlayerState.Paused:
                await player.ResumeAsync();
                logger.LogInformation("[MusicService:{GuildId}] Resumed playback by {User}", player.GuildId,
                    requester.Username);
                return (true,
                    $"Resumed: {player.CurrentTrack.Title.AsMarkdownLink(player.CurrentTrack.Uri?.ToString())}");
            default:
                return (false, "Cannot pause or resume in the current state.");
        }
    }

    // --- Queue Specific Methods ---
    public (Embed? Embed, MessageComponent? Components, string? ErrorMessage) BuildQueueEmbed(
        CustomPlayer player,
        int currentPage, // 0-based for this method, 1-based for display
        ulong interactionMessageId,
        ulong requesterId)
    {
        if (player.CurrentTrack is null && player.Queue.IsEmpty) return (null, null, "The queue is empty.");

        var embed = new EmbedBuilder().WithColor(0xFFA31A); // Orange

        if (player.CurrentTrack != null)
            embed.WithTitle("Now Playing")
                .WithDescription($"{player.CurrentTrack.Title.AsMarkdownLink(player.CurrentTrack.Uri?.ToString())}");
        else
            embed.WithTitle("Queue is Empty");

        var queueCount = player.Queue.Count;
        var totalPages = 0;

        // Adjust currentPage for 0-based internal logic if it was passed as 1-based
        if (currentPage > 0) currentPage--;

        if (queueCount > 0)
        {
            totalPages = (int)Math.Ceiling((double)queueCount / QueueItemsPerPage);
            if (currentPage < 0) currentPage = 0;
            if (currentPage >= totalPages) currentPage = totalPages - 1;
            if (totalPages == 0) totalPages = 1;

            var first = currentPage * QueueItemsPerPage;
            var last = Math.Min(first + QueueItemsPerPage, queueCount);


            var sb = new StringBuilder();
            for (var i = first; i < last; i++)
            {
                var trackItem = player.Queue[i];
                if (trackItem.Track is not null)
                    sb.AppendLine($"{i + 1}. {trackItem.Track.Title.AsMarkdownLink(trackItem.Track.Uri?.ToString())}");
            }

            embed.AddField($"Next Up ({currentPage + 1}/{totalPages})", sb.Length > 0 ? sb.ToString() : "\u200B");
        }

        var loopStatus = player.RepeatMode switch
        {
            TrackRepeatMode.Queue => "Looping through all songs",
            TrackRepeatMode.Track => "Looping Current Song",
            _ => "Loop Disabled"
        };
        var totalSongsInQueueSystem = (player.CurrentTrack != null ? 1 : 0) + queueCount;
        embed.WithFooter(
            $"{loopStatus} | {totalSongsInQueueSystem} Song{(totalSongsInQueueSystem != 1 ? "s" : "")} in Queue");

        var components = new ComponentBuilder();
        if (totalPages <= 1) return (embed.Build(), components.Build(), null);
        components.WithButton("◀",
            $"assistant:queue_page_action:{requesterId}:{interactionMessageId}:{currentPage + 1}:prev",
            ButtonStyle.Secondary);
        components.WithButton("▶",
            $"assistant:queue_page_action:{requesterId}:{interactionMessageId}:{currentPage + 1}:next",
            ButtonStyle.Secondary);

        return (embed.Build(), components.Build(), null);
    }

    public async Task<(bool Success, LavalinkTrack? RemovedTrack, string Message)> RemoveFromQueueAsync(
        CustomPlayer player, int oneBasedIndex)
    {
        if (player.Queue.IsEmpty) return (false, null, "The queue is empty.");
        if (oneBasedIndex <= 0 || oneBasedIndex > player.Queue.Count) return (false, null, "Invalid index.");

        var trackToRemove = player.Queue[oneBasedIndex - 1].Track;
        await player.Queue.RemoveAtAsync(oneBasedIndex - 1);

        var message = trackToRemove != null
            ? $"Removed {trackToRemove.Title.AsMarkdownLink(trackToRemove.Uri?.ToString())} from the queue."
            : "Removed song from the queue.";

        logger.LogInformation("[MusicService:{GuildId}] Removed track at index {Index} from queue.", player.GuildId,
            oneBasedIndex);

        if (player.Queue.IsEmpty && player.CurrentTrack == null && QueueEmptied != null)
            await QueueEmptied.Invoke(player.GuildId, player);
        return (true, trackToRemove, message);
    }

    public async Task<(bool Success, string Message)> ClearQueueAsync(CustomPlayer player)
    {
        if (player.Queue.IsEmpty) return (false, "The queue is already empty.");
        var guildId = player.GuildId;

        await player.Queue.ClearAsync();
        logger.LogInformation("[MusicService:{GuildId}] Queue cleared.", guildId);

        if (QueueEmptied != null)
            await QueueEmptied.Invoke(guildId, player);
        return (true, "Queue cleared.");
    }

    public async Task<(bool Success, string Message)> ShuffleQueueAsync(CustomPlayer player)
    {
        if (player.Queue.Count < 2) return (false, "Not enough songs in the queue to shuffle.");
        await player.Queue.ShuffleAsync();
        logger.LogInformation("[MusicService:{GuildId}] Queue shuffled.", player.GuildId);
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
        await player.Queue.RemoveAtAsync(fromOneBasedIndex - 1);
        await player.Queue.InsertAsync(toOneBasedIndex - 1, trackToMoveItem);

        var message = trackToMoveItem.Track != null
            ? $"Moved {trackToMoveItem.Track.Title.AsMarkdownLink(trackToMoveItem.Track.Uri?.ToString())} from position `{fromOneBasedIndex}` to `{toOneBasedIndex}`."
            : $"Moved song from position `{fromOneBasedIndex}` to `{toOneBasedIndex}`.";
        logger.LogInformation("[MusicService:{GuildId}] Moved track from {FromIndex} to {ToIndex} in queue.",
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

        logger.LogInformation("[MusicService:{GuildId}] Queue loop mode set to {NewMode}.", player.GuildId, newMode);
        return (newMode, message);
    }
}