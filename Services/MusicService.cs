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
        else
            logger.LogDebug(
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
        return (null, PlayerRetrieveStatus.UserNotInVoiceChannel);
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
                replyMessage = $"Looping all {player.Queue.Count} tracks in the queue.";
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
        if (volumePercentage is < 0 or > 200)
            return (false, "Volume out of range. Please use a value between 0 and 200.");

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
        await player.Queue.ClearAsync();
        await player
            .StopAsync(); // This also disconnects if inactivity tracking is configured, or call player.DisconnectAsync() explicitly
        // Lavalink4NET's default inactivity tracking handles disconnect. If you want explicit disconnect:
        // await player.DisconnectAsync();
        logger.LogInformation("[MusicService:{GuildId}] Stopped and disconnected by {User}", player.GuildId,
            requester.Username);
    }

    public async Task<(bool Success, string Message)> SeekAsync(CustomPlayer player, IUser requester, string timeInput)
    {
        if (player.CurrentTrack is null) return (false, "I am not playing anything right now.");

        var timeSpan = TimeUtils.ParseTimestamp(timeInput);
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

        if (index < 0) index = player.Queue.Count + index + 1; // Allow negative indexing from end

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

        // To skip to a track, we remove it, then insert it at the beginning of the queue,
        // then add the previously current track back at its original position (if it wasn't the one being skipped to),
        // and finally, call SkipAsync.
        await player.Queue.RemoveAtAsync(index - 1);
        await player.Queue.InsertAsync(0,
            new TrackQueueItem(trackToPlayItem.Track)); // Insert the target track at the front

        // If the original current track needs to be preserved in the queue
        // (e.g., if repeat queue is on, or just for a cleaner history),
        // you might re-add it. For simple skip-to, this is optional.
        // For this implementation, we assume skip-to means it becomes the next track.
        // Original currentTrack is implicitly handled by SkipAsync moving to the new front of queue.

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
}