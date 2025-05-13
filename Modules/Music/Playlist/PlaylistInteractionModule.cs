using System.Collections.Concurrent;
using System.Text;
using Assistant.Net.Models;
using Assistant.Net.Models.Music;
using Assistant.Net.Models.Playlist;
using Assistant.Net.Modules.Music.Helpers;
using Assistant.Net.Services;
using Assistant.Net.Utilities;
using Discord;
using Discord.Interactions;
using Lavalink4NET;
using Lavalink4NET.Clients;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.Rest.Entities.Tracks;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Music.Playlist;

[Group("playlist", "Manage your music playlists.")]
[CommandContextType(InteractionContextType.Guild)]
public class PlaylistInteractionModule(
    PlaylistService playlistService,
    MusicService musicService,
    IAudioService audioService,
    ILogger<PlaylistInteractionModule> logger)
    : InteractionModuleBase<SocketInteractionContext>
{
    private const int SongsPerPage = 10;
    private static readonly ConcurrentDictionary<ulong, IUserMessage> ActiveShowViews = new();

    // --- Create ---
    [SlashCommand("create", "Create a new playlist.")]
    public async Task CreatePlaylistCommand(
        [Summary("name", "The name for your new playlist (1-100 characters).")]
        string name)
    {
        await DeferAsync();
        var result = await playlistService.CreatePlaylistAsync(Context.User.Id, Context.Guild.Id, name);
        await FollowupAsync(result.Message, ephemeral: !result.Success);
    }

    // --- Delete ---
    [SlashCommand("delete", "Delete one of your playlists.")]
    public async Task DeletePlaylistCommand(
        [Summary("name", "The name of the playlist to delete.")]
        [Autocomplete(typeof(PlaylistNameAutocompleteProvider))]
        string name)
    {
        await DeferAsync(true);
        var playlist = await playlistService.GetPlaylistAsync(Context.User.Id, Context.Guild.Id, name);
        if (playlist == null)
        {
            await FollowupAsync("Playlist not found.", ephemeral: true);
            return;
        }

        var confirmId = $"playlist:delete_confirm:{Context.User.Id}:{name}";
        var cancelId = $"playlist:delete_cancel:{Context.User.Id}:{name}";

        var components = new ComponentBuilder()
            .WithButton("Yes, Delete", confirmId, ButtonStyle.Danger)
            .WithButton("No, Cancel", cancelId, ButtonStyle.Secondary)
            .Build();

        await FollowupAsync($"Are you sure you want to delete playlist '{name}'? This action cannot be undone.",
            components: components, ephemeral: true);
    }

    [ComponentInteraction("playlist:delete_confirm:*:*", true)]
    public async Task HandleDeleteConfirm(ulong userId, string playlistName)
    {
        if (Context.User.Id != userId)
        {
            await RespondAsync("This confirmation is not for you.", ephemeral: true);
            return;
        }

        await DeferAsync(true);
        var success = await playlistService.DeletePlaylistAsync(userId, Context.Guild.Id, playlistName);
        if (success)
            await ModifyOriginalResponseAsync(props =>
            {
                props.Content = $"Playlist '{playlistName}' deleted.";
                props.Components = null;
            });
        else
            await ModifyOriginalResponseAsync(props =>
            {
                props.Content = $"Failed to delete playlist '{playlistName}'. It might have been already deleted.";
                props.Components = null;
            });
    }

    [ComponentInteraction("playlist:delete_cancel:*:*", true)]
    public async Task HandleDeleteCancel(ulong userId, string playlistName)
    {
        if (Context.User.Id != userId)
        {
            await RespondAsync("This confirmation is not for you.", ephemeral: true);
            return;
        }

        await ModifyOriginalResponseAsync(props =>
        {
            props.Content = "Playlist deletion cancelled.";
            props.Components = null;
        });
    }

    // --- Add ---
    [SlashCommand("add", "Add a song or playlist URL to your playlist.")]
    public async Task AddToPlaylistCommand(
        [Summary("playlist_name", "The name of your playlist.")]
        [Autocomplete(typeof(PlaylistNameAutocompleteProvider))]
        string playlistName,
        [Summary("query", "Song name, URL, or playlist URL. Leave empty to add current song.")]
        string? query = null)
    {
        await DeferAsync();

        var (player, retrieveStatus) = await musicService.GetPlayerForContextAsync(
            Context.Guild, Context.User, Context.Channel,
            !string.IsNullOrWhiteSpace(query) ? PlayerChannelBehavior.Join : PlayerChannelBehavior.None,
            MemberVoiceStateBehavior.RequireSame // User must be in a VC to add songs
        );

        if (player == null && !string.IsNullOrWhiteSpace(query))
        {
            await FollowupAsync(MusicModuleHelpers.GetPlayerRetrieveErrorMessage(retrieveStatus), ephemeral: true);
            return;
        }

        if (player == null && string.IsNullOrWhiteSpace(query))
        {
            await FollowupAsync("I'm not connected to a voice channel to get the current song.", ephemeral: true);
            return;
        }


        var songsToAdd = new List<SongModel>();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var loadResult = await musicService.LoadAndQueueTrackAsync(player!, query, Context.User);

            if (loadResult.Status == TrackLoadStatus.LoadFailed || loadResult.Status == TrackLoadStatus.NoMatches ||
                loadResult.Tracks.Count == 0)
            {
                await FollowupAsync(loadResult.ErrorMessage ?? "No tracks found for your query.", ephemeral: true);
                return;
            }

            songsToAdd.AddRange(loadResult.Tracks.Select(track => new SongModel
            {
                Title = track.Title,
                Artist = track.Author,
                Uri = track.Uri?.ToString() ?? "Unknown URI",
                Duration = track.Duration.TotalMilliseconds,
                Thumbnail = track.ArtworkUri?.ToString(),
                Source = track.SourceName ?? "other"
            }));
        }
        else if (player?.CurrentTrack != null)
        {
            var currentTrack = player.CurrentTrack;
            songsToAdd.Add(new SongModel
            {
                Title = currentTrack.Title,
                Artist = currentTrack.Author,
                Uri = currentTrack.Uri?.ToString() ?? "Unknown URI",
                Duration = currentTrack.Duration.TotalMilliseconds,
                Thumbnail = currentTrack.ArtworkUri?.ToString(),
                Source = currentTrack.SourceName ?? "other"
            });
        }
        else
        {
            await FollowupAsync("Please provide a song/URL, or play a song to add the current one.", ephemeral: true);
            return;
        }

        if (songsToAdd.Count == 0)
        {
            await FollowupAsync("No valid songs found to add.", ephemeral: true);
            return;
        }

        var addResult =
            await playlistService.AddTracksToPlaylistAsync(Context.User.Id, Context.Guild.Id, playlistName, songsToAdd);
        await FollowupAsync(addResult.Message, ephemeral: !addResult.Success);
    }

    // --- Remove ---
    [SlashCommand("remove", "Remove a song from your playlist by its position.")]
    public async Task RemoveFromPlaylistCommand(
        [Summary("playlist_name", "The name of your playlist.")]
        [Autocomplete(typeof(PlaylistNameAutocompleteProvider))]
        string playlistName,
        [Summary("position", "The 1-based position of the song to remove.")]
        int position)
    {
        await DeferAsync();
        var result =
            await playlistService.RemoveSongFromPlaylistAsync(Context.User.Id, Context.Guild.Id, playlistName,
                position);
        await FollowupAsync(result.Message, ephemeral: !result.Success);
    }

    // --- List ---
    [SlashCommand("list", "Show all your playlists in this server.")]
    public async Task ListPlaylistsCommand()
    {
        await DeferAsync();
        var playlists = await playlistService.GetUserPlaylistsAsync(Context.User.Id, Context.Guild.Id);
        if (playlists.Count == 0)
        {
            await FollowupAsync("You don't have any playlists in this server.", ephemeral: true);
            return;
        }

        var embed = new EmbedBuilder()
            .WithTitle($"{Context.User.GlobalName ?? Context.User.Username}'s Playlists")
            .WithColor(Color.Blue)
            .WithAuthor(Context.User);

        var description = string.Join("\n",
            playlists.Select((p, i) => $"{i + 1}. {p.Name.Truncate(50)} ({p.Songs.Count} songs)"));
        embed.WithDescription(description.Length > 0 ? description : "No playlists found.");

        await FollowupAsync(embed: embed.Build());
    }

    // --- Show ---
    [SlashCommand("show", "Display the songs in one of your playlists.")]
    public async Task ShowPlaylistCommand(
        [Summary("playlist_name", "The name of the playlist.")] [Autocomplete(typeof(PlaylistNameAutocompleteProvider))]
        string playlistName,
        [Summary("page", "The page number to display (default: 1).")]
        int page = 1)
    {
        await DeferAsync();
        var playlist = await playlistService.GetPlaylistAsync(Context.User.Id, Context.Guild.Id, playlistName);

        if (playlist == null)
        {
            await FollowupAsync("Playlist not found.", ephemeral: true);
            return;
        }

        if (playlist.Songs.Count == 0)
        {
            await FollowupAsync($"Playlist '{playlistName}' is empty.", ephemeral: true);
            return;
        }

        var (embed, components) = BuildShowPlaylistResponse(playlist, page, Context.User.Id);
        var message = await FollowupAsync(embed: embed, components: components);
        if (message != null && components != null)
        {
            ActiveShowViews[message.Id] = message;
            _ = Task.Delay(TimeSpan.FromMinutes(5)).ContinueWith(async _ =>
            {
                if (ActiveShowViews.TryRemove(message.Id, out var msgToClean))
                    try
                    {
                        await msgToClean.ModifyAsync(m => m.Components = new ComponentBuilder().Build());
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug(ex, "Failed to disable components on timed-out show playlist view {MessageId}",
                            msgToClean.Id);
                    }
            });
        }
    }

    private (Embed Embed, MessageComponent? Components) BuildShowPlaylistResponse(PlaylistModel playlist,
        int currentPage, ulong requesterId)
    {
        var totalSongs = playlist.Songs.Count;
        var totalPages = (int)Math.Ceiling((double)totalSongs / SongsPerPage);
        currentPage = Math.Clamp(currentPage, 1, totalPages > 0 ? totalPages : 1);

        var embed = new EmbedBuilder()
            .WithTitle($"Playlist: {playlist.Name.Truncate(100)}")
            .WithColor(Color.Green)
            .WithAuthor(Context.User)
            .WithFooter(
                $"Page {currentPage}/{totalPages} | Total Songs: {totalSongs} | Updated: {TimestampTag.FromDateTime(playlist.UpdatedAt, TimestampTagStyles.Relative)}");

        var songsOnPage = playlist.Songs
            .Skip((currentPage - 1) * SongsPerPage)
            .Take(SongsPerPage)
            .ToList();

        if (songsOnPage.Count != 0)
        {
            var description = new StringBuilder();
            for (var i = 0; i < songsOnPage.Count; i++)
            {
                var song = songsOnPage[i];
                var overallIndex = (currentPage - 1) * SongsPerPage + i + 1;
                description.AppendLine(
                    $"{overallIndex}. {song.Title.AsMarkdownLink(song.Uri).Truncate(80)} (`{TimeSpan.FromMilliseconds(song.Duration):mm\\:ss}`)");
            }

            embed.WithDescription(description.ToString());
        }
        else
        {
            embed.WithDescription(totalSongs > 0 ? "This page is empty." : "This playlist is empty.");
        }

        MessageComponent? components = null;
        if (totalPages <= 1) return (embed.Build(), components);
        var cb = new ComponentBuilder()
            .WithButton("Previous", $"playlist:show_prev:{requesterId}:{playlist.Name}:{currentPage}",
                ButtonStyle.Secondary, disabled: currentPage == 1)
            .WithButton("Next", $"playlist:show_next:{requesterId}:{playlist.Name}:{currentPage}",
                ButtonStyle.Secondary, disabled: currentPage == totalPages);
        components = cb.Build();

        return (embed.Build(), components);
    }

    [ComponentInteraction("playlist:show_prev:*:*:*", true)]
    public async Task HandleShowPrev(ulong requesterId, string playlistName, int currentPage)
    {
        if (Context.User.Id != requesterId)
        {
            await RespondAsync("This interaction is not for you.", ephemeral: true);
            return;
        }

        await DeferAsync();
        var playlist = await playlistService.GetPlaylistAsync(requesterId, Context.Guild.Id, playlistName);
        if (playlist == null)
        {
            await ModifyOriginalResponseAsync(m =>
            {
                m.Content = "Playlist was deleted.";
                m.Embed = null;
                m.Components = null;
            });
            return;
        }

        var (embed, components) = BuildShowPlaylistResponse(playlist, currentPage - 1, requesterId);
        await ModifyOriginalResponseAsync(m =>
        {
            m.Embed = embed;
            m.Components = components;
        });
    }

    [ComponentInteraction("playlist:show_next:*:*:*", true)]
    public async Task HandleShowNext(ulong requesterId, string playlistName, int currentPage)
    {
        if (Context.User.Id != requesterId)
        {
            await RespondAsync("This interaction is not for you.", ephemeral: true);
            return;
        }

        await DeferAsync(); // Defer update
        var playlist = await playlistService.GetPlaylistAsync(requesterId, Context.Guild.Id, playlistName);
        if (playlist == null)
        {
            await ModifyOriginalResponseAsync(m =>
            {
                m.Content = "Playlist was deleted.";
                m.Embed = null;
                m.Components = null;
            });
            return;
        }

        var (embed, components) = BuildShowPlaylistResponse(playlist, currentPage + 1, requesterId);
        await ModifyOriginalResponseAsync(m =>
        {
            m.Embed = embed;
            m.Components = components;
        });
    }

    // --- Play ---
    [SlashCommand("play", "Plays one of your playlists.")]
    public async Task PlayPlaylistCommand(
        [Summary("playlist_name", "The name of the playlist to play.")]
        [Autocomplete(typeof(PlaylistNameAutocompleteProvider))]
        string playlistName)
    {
        await DeferAsync();
        var playlist = await playlistService.GetPlaylistAsync(Context.User.Id, Context.Guild.Id, playlistName);
        if (playlist == null)
        {
            await FollowupAsync("Playlist not found.", ephemeral: true);
            return;
        }

        if (playlist.Songs.Count == 0)
        {
            await FollowupAsync($"Playlist '{playlistName}' is empty.", ephemeral: true);
            return;
        }

        var (player, retrieveStatus) = await musicService.GetPlayerForContextAsync(Context.Guild, Context.User,
            Context.Channel, PlayerChannelBehavior.Join, MemberVoiceStateBehavior.RequireSame);
        if (player == null)
        {
            await FollowupAsync(MusicModuleHelpers.GetPlayerRetrieveErrorMessage(retrieveStatus), ephemeral: true);
            return;
        }

        await player.Queue.ClearAsync();
        var addedCount = 0;
        var failedTracks = new List<string>();

        foreach (var song in playlist.Songs)
        {
            if (string.IsNullOrWhiteSpace(song.Uri))
            {
                logger.LogWarning("Skipping song '{SongTitle}' from playlist '{PlaylistName}' due to missing URI.",
                    song.Title, playlistName);
                failedTracks.Add(song.Title.Truncate(30) + " (Missing URI)");
                continue;
            }

            try
            {
                var trackLoadResult = await audioService.Tracks.LoadTracksAsync(song.Uri, TrackSearchMode.None);
                if (trackLoadResult.Track != null)
                {
                    await player.Queue.AddAsync(new TrackQueueItem(trackLoadResult.Track));
                    addedCount++;
                }
                else
                {
                    logger.LogWarning(
                        "Failed to load track '{TrackUri}' (Title: {SongTitle}) from playlist '{PlaylistName}'.",
                        song.Uri, song.Title, playlistName);
                    failedTracks.Add(song.Title.Truncate(30) + " (Load Failed)");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Error loading track '{TrackUri}' (Title: {SongTitle}) for playlist '{PlaylistName}'", song.Uri,
                    song.Title, playlistName);
                failedTracks.Add(song.Title.Truncate(30) + " (Error)");
            }
        }

        if (addedCount == 0)
        {
            await FollowupAsync($"No songs could be loaded from playlist '{playlistName}'. Check logs for details.",
                ephemeral: true);
            return;
        }

        await musicService.StartPlaybackIfNeededAsync(player);

        var responseMessage = $"Playing {addedCount} songs from playlist '{playlistName}'.";
        if (failedTracks.Count != 0)
            responseMessage +=
                $"\nCould not load {failedTracks.Count} song(s): {string.Join(", ", failedTracks).Truncate(200)}";
        await FollowupAsync(responseMessage);
    }

    // --- Rename ---
    [SlashCommand("rename", "Rename one of your playlists.")]
    public async Task RenamePlaylistCommand(
        [Summary("old_name", "The current name of the playlist.")]
        [Autocomplete(typeof(PlaylistNameAutocompleteProvider))]
        string oldName,
        [Summary("new_name", "The new name for the playlist (1-100 characters).")]
        string newName)
    {
        await DeferAsync();
        var result = await playlistService.RenamePlaylistAsync(Context.User.Id, Context.Guild.Id, oldName, newName);
        await FollowupAsync(result.Message, ephemeral: !result.Success);
    }

    // --- Shuffle ---
    [SlashCommand("shuffle", "Shuffle the songs within one of your playlists.")]
    public async Task ShufflePlaylistCommand(
        [Summary("playlist_name", "The name of the playlist to shuffle.")]
        [Autocomplete(typeof(PlaylistNameAutocompleteProvider))]
        string playlistName)
    {
        await DeferAsync();
        var result = await playlistService.ShufflePlaylistAsync(Context.User.Id, Context.Guild.Id, playlistName);
        await FollowupAsync(result.Message, ephemeral: !result.Success);
    }

    // --- Share ---
    [SlashCommand("share", "Share one of your playlists with another user in this server.")]
    public async Task SharePlaylistCommand(
        [Summary("playlist_name", "The name of your playlist to share.")]
        [Autocomplete(typeof(PlaylistNameAutocompleteProvider))]
        string playlistName,
        [Summary("user", "The user to share the playlist with.")]
        IUser user)
    {
        if (user.IsBot)
        {
            await RespondAsync("You cannot share playlists with bots.", ephemeral: true);
            return;
        }

        if (user.Id == Context.User.Id)
        {
            await RespondAsync("You cannot share a playlist with yourself.", ephemeral: true);
            return;
        }

        await DeferAsync(true);
        var result = await playlistService.SharePlaylistAsync(Context.User.Id, Context.Guild.Id, playlistName, user.Id);
        await FollowupAsync(result.Message, ephemeral: true);

        if (result.Success)
            try
            {
                await Context.Channel.SendMessageAsync(
                    $"{Context.User.Mention} shared the playlist '{playlistName.Truncate(50)}' with {user.Mention}!",
                    allowedMentions: AllowedMentions.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send public share confirmation message for playlist '{PlaylistName}'",
                    playlistName);
            }
    }
}