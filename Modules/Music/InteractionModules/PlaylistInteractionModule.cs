using System.Collections.Concurrent;
using System.Text;
using Assistant.Net.Models.Music;
using Assistant.Net.Models.Playlist;
using Assistant.Net.Modules.Music.Autocomplete;
using Assistant.Net.Modules.Music.Logic;
using Assistant.Net.Modules.Music.Logic.Player;
using Assistant.Net.Services.Music;
using Assistant.Net.Utilities;
using Discord;
using Discord.Interactions;
using Lavalink4NET;
using Lavalink4NET.Clients;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.Rest.Entities.Tracks;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Music.InteractionModules;

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
        await DeferAsync().ConfigureAwait(false);
        var result = await playlistService.CreatePlaylistAsync(Context.User.Id, Context.Guild.Id, name)
            .ConfigureAwait(false);

        if (result.Success)
        {
            var container = new ContainerBuilder()
                .WithTextDisplay(new TextDisplayBuilder("✅ **Playlist Created**"))
                .WithTextDisplay(new TextDisplayBuilder($"Your new playlist **'{name}'** is ready."));

            var components = new ComponentBuilderV2().WithContainer(container).Build();
            await FollowupAsync(components: components, flags: MessageFlags.ComponentsV2).ConfigureAwait(false);
        }
        else
        {
            await FollowupAsync(result.Message, ephemeral: true).ConfigureAwait(false);
        }
    }

    // --- Delete ---
    [SlashCommand("delete", "Delete one of your playlists.")]
    public async Task DeletePlaylistCommand(
        [Summary("name", "The name of the playlist to delete.")]
        [Autocomplete(typeof(PlaylistNameAutocompleteProvider))]
        string name)
    {
        await DeferAsync(true).ConfigureAwait(false);
        var playlist = await playlistService.GetPlaylistAsync(Context.User.Id, Context.Guild.Id, name)
            .ConfigureAwait(false);
        if (playlist == null)
        {
            await FollowupAsync("Playlist not found.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var confirmId = $"playlist:delete_confirm:{Context.User.Id}:{name}";
        var cancelId = $"playlist:delete_cancel:{Context.User.Id}:{name}";

        var container = new ContainerBuilder()
            .WithAccentColor(Color.Red)
            .WithTextDisplay(new TextDisplayBuilder("⚠️ **Are you sure?**"))
            .WithTextDisplay(
                new TextDisplayBuilder($"Do you want to delete the playlist **'{name}'**? This cannot be undone."))
            .WithActionRow(row =>
            {
                row.WithButton("Yes, Delete", confirmId, ButtonStyle.Danger);
                row.WithButton("No, Cancel", cancelId, ButtonStyle.Secondary);
            });

        var components = new ComponentBuilderV2().WithContainer(container).Build();
        await FollowupAsync(components: components, ephemeral: true, flags: MessageFlags.ComponentsV2)
            .ConfigureAwait(false);
    }

    [ComponentInteraction("playlist:delete_confirm:*:*", true)]
    public async Task HandleDeleteConfirm(ulong userId, string playlistName)
    {
        if (Context.User.Id != userId)
        {
            await RespondAsync("This confirmation is not for you.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync(true).ConfigureAwait(false);
        var success = await playlistService.DeletePlaylistAsync(userId, Context.Guild.Id, playlistName)
            .ConfigureAwait(false);

        var container = new ContainerBuilder();
        if (success)
            container
                .WithTextDisplay(new TextDisplayBuilder($"✅ Playlist **'{playlistName}'** has been deleted."));
        else
            container.WithAccentColor(Color.Red)
                .WithTextDisplay(
                    new TextDisplayBuilder(
                        $"❌ Failed to delete **'{playlistName}'**. It might have been deleted already."));

        var components = new ComponentBuilderV2().WithContainer(container).Build();
        await ModifyOriginalResponseAsync(props =>
        {
            props.Content = ""; // Clear original text content
            props.Components = components;
            props.Flags = MessageFlags.ComponentsV2;
        }).ConfigureAwait(false);
    }

    [ComponentInteraction("playlist:delete_cancel:*:*", true)]
    public async Task HandleDeleteCancel(ulong userId, string playlistName)
    {
        if (Context.User.Id != userId)
        {
            await RespondAsync("This confirmation is not for you.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var container = new ContainerBuilder()
            .WithTextDisplay(new TextDisplayBuilder($"↪️ Playlist deletion for **'{playlistName}'** was cancelled."));

        var components = new ComponentBuilderV2().WithContainer(container).Build();
        await ModifyOriginalResponseAsync(props =>
        {
            props.Content = "";
            props.Components = components;
            props.Flags = MessageFlags.ComponentsV2;
        }).ConfigureAwait(false);
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
        await DeferAsync().ConfigureAwait(false);

        var (player, retrieveStatus) = await musicService.GetPlayerForContextAsync(
            Context.Guild, Context.User, Context.Channel,
            !string.IsNullOrWhiteSpace(query) ? PlayerChannelBehavior.Join : PlayerChannelBehavior.None,
            MemberVoiceStateBehavior.RequireSame // User must be in a VC to add songs
        ).ConfigureAwait(false);

        switch (player)
        {
            case null when !string.IsNullOrWhiteSpace(query):
                await FollowupAsync(MusicModuleHelpers.GetPlayerRetrieveErrorMessage(retrieveStatus), ephemeral: true)
                    .ConfigureAwait(false);
                return;
            case null when string.IsNullOrWhiteSpace(query):
                await FollowupAsync("I'm not connected to a voice channel to get the current song.", ephemeral: true)
                    .ConfigureAwait(false);
                return;
        }


        var songsToAdd = new List<SongModel>();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var loadResult = await musicService.LoadAndQueueTrackAsync(player!, query, Context.User)
                .ConfigureAwait(false);

            if (loadResult.Status == TrackLoadStatus.LoadFailed || loadResult.Status == TrackLoadStatus.NoMatches ||
                loadResult.Tracks.Count == 0)
            {
                await FollowupAsync(loadResult.ErrorMessage ?? "No tracks found for your query.", ephemeral: true)
                    .ConfigureAwait(false);
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
            await FollowupAsync("Please provide a song/URL, or play a song to add the current one.", ephemeral: true)
                .ConfigureAwait(false);
            return;
        }

        if (songsToAdd.Count == 0)
        {
            await FollowupAsync("No valid songs found to add.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var addResult =
            await playlistService.AddTracksToPlaylistAsync(Context.User.Id, Context.Guild.Id, playlistName, songsToAdd)
                .ConfigureAwait(false);

        if (addResult.Success)
        {
            var container = new ContainerBuilder()
                .WithTextDisplay(new TextDisplayBuilder("🎶 **Added to Playlist**"))
                .WithTextDisplay(new TextDisplayBuilder(addResult.Message));

            var components = new ComponentBuilderV2().WithContainer(container).Build();
            await FollowupAsync(components: components, flags: MessageFlags.ComponentsV2).ConfigureAwait(false);
        }
        else
        {
            await FollowupAsync(addResult.Message, ephemeral: true).ConfigureAwait(false);
        }
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
        await DeferAsync().ConfigureAwait(false);
        var (success, message, removedSong) =
            await playlistService.RemoveSongFromPlaylistAsync(Context.User.Id, Context.Guild.Id, playlistName,
                position).ConfigureAwait(false);

        if (success && removedSong != null)
        {
            var container = new ContainerBuilder()
                .WithTextDisplay(new TextDisplayBuilder("🗑️ **Song Removed**"))
                .WithTextDisplay(new TextDisplayBuilder(message));
            var components = new ComponentBuilderV2().WithContainer(container).Build();
            await FollowupAsync(components: components, flags: MessageFlags.ComponentsV2).ConfigureAwait(false);
        }
        else
        {
            await FollowupAsync(message, ephemeral: true).ConfigureAwait(false);
        }
    }

    // --- List ---
    [SlashCommand("list", "Show all your playlists in this server.")]
    public async Task ListPlaylistsCommand()
    {
        await DeferAsync().ConfigureAwait(false);
        var playlists = await playlistService.GetUserPlaylistsAsync(Context.User.Id, Context.Guild.Id)
            .ConfigureAwait(false);

        var container = new ContainerBuilder();

        var header = new SectionBuilder()
            .AddComponent(
                new TextDisplayBuilder($"# 🎵 {Context.User.GlobalName ?? Context.User.Username}'s Playlists"))
            .WithAccessory(new ThumbnailBuilder
            {
                Media = new UnfurledMediaItemProperties
                    { Url = Context.User.GetDisplayAvatarUrl() ?? Context.User.GetDefaultAvatarUrl() }
            });

        container.WithSection(header).WithSeparator();

        if (playlists.Count == 0)
        {
            container.WithTextDisplay(
                new TextDisplayBuilder("You don't have any playlists yet. Use `/playlist create` to make one!"));
        }
        else
        {
            var description = string.Join("\n",
                playlists.Select((p, i) =>
                    $"{i + 1}. **{p.Name.Truncate(50)}** ({p.Songs.Count} song{(p.Songs.Count == 1 ? "" : "s")})"));
            container.WithTextDisplay(new TextDisplayBuilder(description));
        }

        var components = new ComponentBuilderV2().WithContainer(container).Build();
        await FollowupAsync(components: components, flags: MessageFlags.ComponentsV2).ConfigureAwait(false);
    }

    // --- Show ---
    [SlashCommand("show", "Display the songs in one of your playlists.")]
    public async Task ShowPlaylistCommand(
        [Summary("playlist_name", "The name of the playlist.")] [Autocomplete(typeof(PlaylistNameAutocompleteProvider))]
        string playlistName,
        [Summary("page", "The page number to display (default: 1).")]
        int page = 1)
    {
        await DeferAsync().ConfigureAwait(false);
        var playlist = await playlistService.GetPlaylistAsync(Context.User.Id, Context.Guild.Id, playlistName)
            .ConfigureAwait(false);

        if (playlist == null)
        {
            await FollowupAsync("Playlist not found.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var (components, errorMessage) = BuildShowPlaylistResponse(playlist, page, Context.User);

        if (errorMessage != null)
        {
            await FollowupAsync(errorMessage, ephemeral: true).ConfigureAwait(false);
            return;
        }

        var message = await FollowupAsync(components: components, flags: MessageFlags.ComponentsV2)
            .ConfigureAwait(false);
        if (message == null || components == null) return;
        ActiveShowViews[message.Id] = message;
        _ = Task.Delay(TimeSpan.FromMinutes(5)).ContinueWith(async _ =>
        {
            if (!ActiveShowViews.TryRemove(message.Id, out var msgToClean)) return;
            try
            {
                await msgToClean.ModifyAsync(m => m.Components = new ComponentBuilder().Build())
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to disable components on timed-out show playlist view {MessageId}",
                    msgToClean.Id);
            }
        });
    }

    private (MessageComponent? Components, string? ErrorMessage) BuildShowPlaylistResponse(PlaylistModel playlist,
        int currentPage, IUser requester)
    {
        var totalSongs = playlist.Songs.Count;
        if (totalSongs == 0)
        {
            var emptyContainer = new ContainerBuilder()
                .WithTextDisplay(new TextDisplayBuilder($"**🎵 Playlist: {playlist.Name.Truncate(100)}**"))
                .WithTextDisplay(new TextDisplayBuilder("*This playlist is empty.*"));
            return (new ComponentBuilderV2().WithContainer(emptyContainer).Build(), null);
        }

        var totalPages = (int)Math.Ceiling((double)totalSongs / SongsPerPage);
        currentPage = Math.Clamp(currentPage, 1, totalPages);

        var container = new ContainerBuilder()
            .WithSection(section =>
            {
                section.AddComponent(new TextDisplayBuilder($"**🎵 Playlist: {playlist.Name.Truncate(100)}**"));
                section.AddComponent(new TextDisplayBuilder($"*Created by {requester.Mention}*"));
            })
            .WithSeparator();

        var songsOnPage = playlist.Songs
            .Skip((currentPage - 1) * SongsPerPage)
            .Take(SongsPerPage)
            .ToList();

        var songListBuilder = new StringBuilder();
        for (var i = 0; i < songsOnPage.Count; i++)
        {
            var song = songsOnPage[i];
            var overallIndex = (currentPage - 1) * SongsPerPage + i + 1;
            songListBuilder.AppendLine(
                $"{overallIndex}. {song.Title.AsMarkdownLink(song.Uri).Truncate(80)} (`{TimeSpan.FromMilliseconds(song.Duration):mm\\:ss}`)");
        }

        container.WithTextDisplay(new TextDisplayBuilder(songListBuilder.ToString()));
        container.WithSeparator();

        var footerText =
            $"Page {currentPage}/{totalPages}  •  {totalSongs} Songs  •  Updated {TimestampTag.FormatFromDateTime(playlist.UpdatedAt, TimestampTagStyles.Relative)}";
        container.WithTextDisplay(new TextDisplayBuilder(footerText));

        var controlsRow = new ActionRowBuilder()
            .WithButton("Previous", $"playlist:show_prev:{requester.Id}:{playlist.Name}:{currentPage}",
                ButtonStyle.Secondary, new Emoji("◀"), disabled: currentPage == 1)
            .WithButton("Next", $"playlist:show_next:{requester.Id}:{playlist.Name}:{currentPage}",
                ButtonStyle.Secondary, new Emoji("▶"), disabled: currentPage == totalPages)
            .WithButton("Shuffle", $"playlist:action_shuffle:{requester.Id}:{playlist.Name}", ButtonStyle.Primary,
                new Emoji("🔀"))
            .WithButton("Play", $"playlist:action_play:{requester.Id}:{playlist.Name}", ButtonStyle.Success,
                new Emoji("▶️"));

        container.WithActionRow(controlsRow);
        return (new ComponentBuilderV2().WithContainer(container).Build(), null);
    }

    [ComponentInteraction("playlist:show_prev:*:*:*", true)]
    public async Task HandleShowPrev(ulong requesterId, string playlistName, int currentPage)
    {
        if (Context.User.Id != requesterId)
        {
            await RespondAsync("This interaction is not for you.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync().ConfigureAwait(false);
        var playlist = await playlistService.GetPlaylistAsync(requesterId, Context.Guild.Id, playlistName)
            .ConfigureAwait(false);
        if (playlist == null)
        {
            await ModifyOriginalResponseAsync(m =>
            {
                m.Content = "Playlist was deleted.";
                m.Components = new ComponentBuilder().Build();
            }).ConfigureAwait(false);
            return;
        }

        var (components, _) = BuildShowPlaylistResponse(playlist, currentPage - 1, Context.User);
        await ModifyOriginalResponseAsync(m =>
        {
            m.Components = components;
            m.Flags = MessageFlags.ComponentsV2;
        }).ConfigureAwait(false);
    }

    [ComponentInteraction("playlist:show_next:*:*:*", true)]
    public async Task HandleShowNext(ulong requesterId, string playlistName, int currentPage)
    {
        if (Context.User.Id != requesterId)
        {
            await RespondAsync("This interaction is not for you.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync().ConfigureAwait(false);
        var playlist = await playlistService.GetPlaylistAsync(requesterId, Context.Guild.Id, playlistName)
            .ConfigureAwait(false);
        if (playlist == null)
        {
            await ModifyOriginalResponseAsync(m =>
            {
                m.Content = "Playlist was deleted.";
                m.Components = new ComponentBuilder().Build();
            }).ConfigureAwait(false);
            return;
        }

        var (components, _) = BuildShowPlaylistResponse(playlist, currentPage + 1, Context.User);
        await ModifyOriginalResponseAsync(m =>
        {
            m.Components = components;
            m.Flags = MessageFlags.ComponentsV2;
        }).ConfigureAwait(false);
    }

    [ComponentInteraction("playlist:action_shuffle:*:*", true)]
    public async Task HandleShuffleButtonAsync(ulong requesterId, string playlistName)
    {
        if (Context.User.Id != requesterId)
        {
            await RespondAsync("This interaction is not for you.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync(true).ConfigureAwait(false);

        var result = await playlistService.ShufflePlaylistAsync(requesterId, Context.Guild.Id, playlistName)
            .ConfigureAwait(false);

        if (result is { Success: true, Playlist: not null })
        {
            var container = new ContainerBuilder()
                .WithTextDisplay(new TextDisplayBuilder($"🔀 Shuffled playlist **'{playlistName}'**."));
            var ephemeralComponents = new ComponentBuilderV2().WithContainer(container).Build();
            await FollowupAsync(components: ephemeralComponents, ephemeral: true, flags: MessageFlags.ComponentsV2)
                .ConfigureAwait(false);

            // Update the original message to show the shuffled playlist
            var originalResponse = await GetOriginalResponseAsync().ConfigureAwait(false);
            var (components, _) = BuildShowPlaylistResponse(result.Playlist, 1, Context.User);
            await originalResponse.ModifyAsync(props =>
            {
                props.Components = components;
                props.Flags = MessageFlags.ComponentsV2;
            }).ConfigureAwait(false);
        }
        else
        {
            await FollowupAsync(result.Message, ephemeral: true).ConfigureAwait(false);
        }
    }

    [ComponentInteraction("playlist:action_play:*:*", true)]
    public async Task HandlePlayButtonAsync(ulong requesterId, string playlistName)
    {
        if (Context.User.Id != requesterId)
        {
            await RespondAsync("This interaction is not for you.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync().ConfigureAwait(false);

        var playlist = await playlistService.GetPlaylistAsync(requesterId, Context.Guild.Id, playlistName)
            .ConfigureAwait(false);
        if (playlist == null)
        {
            await FollowupAsync("Playlist not found.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (playlist.Songs.Count == 0)
        {
            await FollowupAsync($"Playlist '{playlistName}' is empty.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var (player, retrieveStatus) = await musicService.GetPlayerForContextAsync(Context.Guild, Context.User,
            Context.Channel, PlayerChannelBehavior.Join, MemberVoiceStateBehavior.RequireSame).ConfigureAwait(false);
        if (player == null)
        {
            await FollowupAsync(MusicModuleHelpers.GetPlayerRetrieveErrorMessage(retrieveStatus), ephemeral: true)
                .ConfigureAwait(false);
            return;
        }

        await player.Queue.ClearAsync().ConfigureAwait(false);
        var (addedCount, failedTracks) = await QueuePlaylistSongsAsync(player, playlist).ConfigureAwait(false);

        if (addedCount == 0)
        {
            await FollowupAsync($"No songs could be loaded from playlist '{playlistName}'. Check logs for details.",
                ephemeral: true).ConfigureAwait(false);
            return;
        }

        await musicService.StartPlaybackIfNeededAsync(player).ConfigureAwait(false);

        var container = new ContainerBuilder()
            .WithTextDisplay(new TextDisplayBuilder("▶️ **Now Playing Playlist**"))
            .WithTextDisplay(
                new TextDisplayBuilder($"Playing **{addedCount}** song(s) from **'{playlistName}'**."));

        if (failedTracks.Count > 0)
            container.WithTextDisplay(new TextDisplayBuilder(
                $"*Could not load {failedTracks.Count} song(s): {string.Join(", ", failedTracks).Truncate(200)}*"));

        var components = new ComponentBuilderV2().WithContainer(container).Build();
        await FollowupAsync(components: components, flags: MessageFlags.ComponentsV2).ConfigureAwait(false);
    }

    // --- Play ---
    [SlashCommand("play", "Plays one of your playlists.")]
    public async Task PlayPlaylistCommand(
        [Summary("playlist_name", "The name of the playlist to play.")]
        [Autocomplete(typeof(PlaylistNameAutocompleteProvider))]
        string playlistName)
    {
        await DeferAsync().ConfigureAwait(false);
        var playlist = await playlistService.GetPlaylistAsync(Context.User.Id, Context.Guild.Id, playlistName)
            .ConfigureAwait(false);
        if (playlist == null)
        {
            await FollowupAsync("Playlist not found.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (playlist.Songs.Count == 0)
        {
            await FollowupAsync($"Playlist '{playlistName}' is empty.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var (player, retrieveStatus) = await musicService.GetPlayerForContextAsync(Context.Guild, Context.User,
            Context.Channel, PlayerChannelBehavior.Join, MemberVoiceStateBehavior.RequireSame).ConfigureAwait(false);
        if (player == null)
        {
            await FollowupAsync(MusicModuleHelpers.GetPlayerRetrieveErrorMessage(retrieveStatus), ephemeral: true)
                .ConfigureAwait(false);
            return;
        }

        await player.Queue.ClearAsync().ConfigureAwait(false);
        var (addedCount, failedTracks) = await QueuePlaylistSongsAsync(player, playlist).ConfigureAwait(false);


        if (addedCount == 0)
        {
            await FollowupAsync($"No songs could be loaded from playlist '{playlistName}'. Check logs for details.",
                ephemeral: true).ConfigureAwait(false);
            return;
        }

        await musicService.StartPlaybackIfNeededAsync(player).ConfigureAwait(false);

        var container = new ContainerBuilder()
            .WithTextDisplay(new TextDisplayBuilder("▶️ **Now Playing Playlist**"))
            .WithTextDisplay(
                new TextDisplayBuilder($"Playing **{addedCount}** song(s) from **'{playlistName}'**."));

        if (failedTracks.Count > 0)
            container.WithTextDisplay(new TextDisplayBuilder(
                $"*Could not load {failedTracks.Count} song(s): {string.Join(", ", failedTracks).Truncate(200)}*"));

        var components = new ComponentBuilderV2().WithContainer(container).Build();
        await FollowupAsync(components: components, flags: MessageFlags.ComponentsV2).ConfigureAwait(false);
    }

    private async Task<(int AddedCount, List<string> FailedTracks)> QueuePlaylistSongsAsync(CustomPlayer player,
        PlaylistModel playlist)
    {
        var addedCount = 0;
        var failedTracks = new List<string>();

        foreach (var song in playlist.Songs)
        {
            if (string.IsNullOrWhiteSpace(song.Uri))
            {
                logger.LogWarning("Skipping song '{SongTitle}' from playlist '{PlaylistName}' due to missing URI.",
                    song.Title, playlist.Name);
                failedTracks.Add(song.Title.Truncate(30) + " (Missing URI)");
                continue;
            }

            try
            {
                var trackLoadResult = await audioService.Tracks.LoadTracksAsync(song.Uri, TrackSearchMode.None)
                    .ConfigureAwait(false);
                if (trackLoadResult.Track != null)
                {
                    await player.Queue.AddAsync(new TrackQueueItem(trackLoadResult.Track)).ConfigureAwait(false);
                    addedCount++;
                }
                else
                {
                    logger.LogWarning(
                        "Failed to load track '{TrackUri}' (Title: {SongTitle}) from playlist '{PlaylistName}'.",
                        song.Uri, song.Title, playlist.Name);
                    failedTracks.Add(song.Title.Truncate(30) + " (Load Failed)");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Error loading track '{TrackUri}' (Title: {SongTitle}) for playlist '{PlaylistName}'", song.Uri,
                    song.Title, playlist.Name);
                failedTracks.Add(song.Title.Truncate(30) + " (Error)");
            }
        }

        return (addedCount, failedTracks);
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
        await DeferAsync().ConfigureAwait(false);
        var result = await playlistService.RenamePlaylistAsync(Context.User.Id, Context.Guild.Id, oldName, newName)
            .ConfigureAwait(false);
        await FollowupAsync(result.Message, ephemeral: !result.Success).ConfigureAwait(false);
    }

    // --- Shuffle ---
    [SlashCommand("shuffle", "Shuffle the songs within one of your playlists.")]
    public async Task ShufflePlaylistCommand(
        [Summary("playlist_name", "The name of the playlist to shuffle.")]
        [Autocomplete(typeof(PlaylistNameAutocompleteProvider))]
        string playlistName)
    {
        await DeferAsync().ConfigureAwait(false);
        var result = await playlistService.ShufflePlaylistAsync(Context.User.Id, Context.Guild.Id, playlistName)
            .ConfigureAwait(false);

        if (result.Success)
        {
            var container = new ContainerBuilder()
                .WithTextDisplay(new TextDisplayBuilder($"🔀 **{result.Message}**"));
            var components = new ComponentBuilderV2().WithContainer(container).Build();
            await FollowupAsync(components: components, flags: MessageFlags.ComponentsV2).ConfigureAwait(false);
        }
        else
        {
            await FollowupAsync(result.Message, ephemeral: true).ConfigureAwait(false);
        }
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
            await RespondAsync("You cannot share playlists with bots.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (user.Id == Context.User.Id)
        {
            await RespondAsync("You cannot share a playlist with yourself.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync(true).ConfigureAwait(false);
        var result = await playlistService.SharePlaylistAsync(Context.User.Id, Context.Guild.Id, playlistName, user.Id)
            .ConfigureAwait(false);
        await FollowupAsync(result.Message, ephemeral: true).ConfigureAwait(false);

        if (result.Success)
            try
            {
                await Context.Channel.SendMessageAsync(
                    $"{Context.User.Mention} shared the playlist '{playlistName.Truncate(50)}' with {user.Mention}!",
                    allowedMentions: AllowedMentions.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send public share confirmation message for playlist '{PlaylistName}'",
                    playlistName);
            }
    }
}