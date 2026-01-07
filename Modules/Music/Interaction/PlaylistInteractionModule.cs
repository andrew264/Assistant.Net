using Assistant.Net.Data.Entities;
using Assistant.Net.Models.Music;
using Assistant.Net.Modules.Shared.Autocomplete;
using Assistant.Net.Services.Data;
using Assistant.Net.Services.Music;
using Assistant.Net.Services.Music.Logic;
using Assistant.Net.Utilities;
using Assistant.Net.Utilities.Ui;
using Discord;
using Discord.Interactions;
using Lavalink4NET;
using Lavalink4NET.Clients;
using Lavalink4NET.Players;
using Lavalink4NET.Rest;
using Lavalink4NET.Rest.Entities.Tracks;
using Lavalink4NET.Tracks;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Music.Interaction;

[Group("playlist", "Manage your music playlists.")]
[CommandContextType(InteractionContextType.Guild)]
public class PlaylistInteractionModule(
    PlaylistService playlistService,
    MusicService musicService,
    IAudioService audioService,
    ILogger<PlaylistInteractionModule> logger) : MusicInteractionModuleBase(musicService, logger)
{
    [SlashCommand("play", "Plays one of your playlists.")]
    public async Task PlayPlaylistCommand(
        [Summary("playlist_name", "The name of the playlist to play.")]
        [Autocomplete(typeof(PlaylistNameAutocompleteProvider))]
        string playlistName)
    {
        await DeferAsync().ConfigureAwait(false);
        var result = await ProcessPlayPlaylistLogic(playlistName, Context.User.Id);
        await FollowupAsync(result.Message, ephemeral: !result.Success).ConfigureAwait(false);
    }

    [SlashCommand("manage", "Open the playlist dashboard to manage your playlists.")]
    public async Task ManagePlaylistsCommand()
    {
        await DeferAsync().ConfigureAwait(false);
        var playlists = await playlistService.GetUserPlaylistsAsync(Context.User.Id, Context.Guild.Id)
            .ConfigureAwait(false);

        var components = PlaylistUiFactory.BuildPlaylistDashboard(playlists, Context.User.Id);
        await FollowupAsync(components: components, flags: MessageFlags.ComponentsV2).ConfigureAwait(false);
    }

    [ComponentInteraction(PlaylistUiFactory.DashSelectId, true)]
    public async Task HandleDashboardSelect(string[] selection)
    {
        if (selection.Length == 0 || !long.TryParse(selection[0], out var playlistId)) return;

        await DeferAsync().ConfigureAwait(false);
        var playlist = await GetPlaylistByIdAsync(playlistId);
        if (playlist == null) return;

        var components = PlaylistUiFactory.BuildPlaylistDetail(playlist, 1);
        await ModifyOriginalResponseAsync(m =>
        {
            m.Components = components;
            m.Flags = MessageFlags.ComponentsV2;
        }).ConfigureAwait(false);
    }

    [ComponentInteraction(PlaylistUiFactory.DashCreateId, true)]
    public async Task HandleDashboardCreateButton()
    {
        var modal = new ModalBuilder()
            .WithTitle("Create Playlist")
            .WithCustomId("playlist:modal:create")
            .AddTextInput("Playlist Name", "name", placeholder: "My Awesome Playlist", maxLength: 100)
            .Build();
        await RespondWithModalAsync(modal).ConfigureAwait(false);
    }

    [ComponentInteraction(PlaylistUiFactory.DashBackId, true)]
    public async Task HandleDashboardBack()
    {
        await DeferAsync().ConfigureAwait(false);
        var playlists = await playlistService.GetUserPlaylistsAsync(Context.User.Id, Context.Guild.Id)
            .ConfigureAwait(false);
        var components = PlaylistUiFactory.BuildPlaylistDashboard(playlists, Context.User.Id);
        await ModifyOriginalResponseAsync(m =>
        {
            m.Components = components;
            m.Flags = MessageFlags.ComponentsV2;
        }).ConfigureAwait(false);
    }

    [ComponentInteraction(PlaylistUiFactory.DashNavPrefix + ":*:*:*", true)]
    public async Task HandleDashboardNavigation(long playlistId, int page, string action)
    {
        await DeferAsync().ConfigureAwait(false);
        var playlist = await GetPlaylistByIdAsync(playlistId);
        if (playlist == null) return;

        var components = PlaylistUiFactory.BuildPlaylistDetail(playlist, page);
        await ModifyOriginalResponseAsync(m =>
        {
            m.Components = components;
            m.Flags = MessageFlags.ComponentsV2;
        }).ConfigureAwait(false);
    }

    [ComponentInteraction(PlaylistUiFactory.DashActionPrefix + ":*:*", true)]
    public async Task HandleDashboardAction(long playlistId, string action)
    {
        if (action.StartsWith("mode_"))
        {
            await HandleModeSwitch(playlistId, action);
            return;
        }

        var playlist = await playlistService.GetPlaylistAsync(Context.User.Id, Context.Guild.Id, playlistId)
            .ConfigureAwait(false);
        if (playlist == null)
        {
            await RespondAsync("Playlist not found or deleted.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        switch (action)
        {
            case "play":
                await DeferAsync().ConfigureAwait(false);
                var playResult = await ProcessPlayPlaylistLogic(playlist.Name, Context.User.Id);
                await FollowupAsync(playResult.Message, ephemeral: true).ConfigureAwait(false);
                break;
            case "shuffle":
                await DeferAsync().ConfigureAwait(false);
                var shuffleResult =
                    await playlistService.ShufflePlaylistAsync(Context.User.Id, Context.Guild.Id, playlist.Name);
                if (shuffleResult is { Success: true, Playlist: not null })
                {
                    var components = PlaylistUiFactory.BuildPlaylistDetail(shuffleResult.Playlist, 1,
                        statusMessage: "Playlist shuffled!");
                    await ModifyOriginalResponseAsync(m =>
                    {
                        m.Components = components;
                        m.Flags = MessageFlags.ComponentsV2;
                    }).ConfigureAwait(false);
                }
                else
                {
                    await FollowupAsync(shuffleResult.Message, ephemeral: true).ConfigureAwait(false);
                }

                break;
            case "add":
                var modal = new ModalBuilder()
                    .WithTitle($"Add to '{playlist.Name.Truncate(20)}'")
                    .WithCustomId($"playlist:modal:add:{playlistId}")
                    .AddTextInput("Song URL or Name", "query", placeholder: "Paste URL or type song name")
                    .Build();
                await RespondWithModalAsync(modal).ConfigureAwait(false);
                break;
            case "rename":
                var renameModal = new ModalBuilder()
                    .WithTitle("Rename Playlist")
                    .WithCustomId($"playlist:modal:rename:{playlistId}")
                    .AddTextInput("New Name", "new_name", value: playlist.Name, maxLength: 100)
                    .Build();
                await RespondWithModalAsync(renameModal).ConfigureAwait(false);
                break;
        }
    }

    private async Task HandleModeSwitch(long playlistId, string modeAction)
    {
        await DeferAsync().ConfigureAwait(false);
        var playlist = await GetPlaylistByIdAsync(playlistId);
        if (playlist == null) return;

        var mode = modeAction switch
        {
            "mode_remove" => PlaylistUiFactory.PlaylistViewMode.Remove,
            "mode_share" => PlaylistUiFactory.PlaylistViewMode.Share,
            "mode_delete" => PlaylistUiFactory.PlaylistViewMode.DeleteConfirm,
            _ => PlaylistUiFactory.PlaylistViewMode.View
        };

        var components = PlaylistUiFactory.BuildPlaylistDetail(playlist, 1, mode);
        await ModifyOriginalResponseAsync(m =>
        {
            m.Components = components;
            m.Flags = MessageFlags.ComponentsV2;
        }).ConfigureAwait(false);
    }

    [ComponentInteraction(PlaylistUiFactory.DashCancelId + ":*", true)]
    public async Task HandleDashboardCancel(long playlistId)
    {
        await DeferAsync().ConfigureAwait(false);
        var playlist = await GetPlaylistByIdAsync(playlistId);
        if (playlist == null) return;

        var components = PlaylistUiFactory.BuildPlaylistDetail(playlist, 1);
        await ModifyOriginalResponseAsync(m =>
        {
            m.Components = components;
            m.Flags = MessageFlags.ComponentsV2;
        }).ConfigureAwait(false);
    }

    [ComponentInteraction(PlaylistUiFactory.DashDeleteConfirmId + ":*", true)]
    public async Task HandleDashboardDeleteConfirm(long playlistId)
    {
        await DeferAsync().ConfigureAwait(false);
        var playlist = await GetPlaylistByIdAsync(playlistId);
        if (playlist == null) return;

        var success = await playlistService.DeletePlaylistAsync(Context.User.Id, Context.Guild.Id, playlist.Name);
        if (success)
        {
            var playlists = await playlistService.GetUserPlaylistsAsync(Context.User.Id, Context.Guild.Id)
                .ConfigureAwait(false);
            var components = PlaylistUiFactory.BuildPlaylistDashboard(playlists, Context.User.Id);
            await ModifyOriginalResponseAsync(m =>
            {
                m.Components = components;
                m.Flags = MessageFlags.ComponentsV2;
            }).ConfigureAwait(false);
            await FollowupAsync($"Playlist '{playlist.Name}' deleted.", ephemeral: true).ConfigureAwait(false);
        }
        else
        {
            await FollowupAsync("Failed to delete playlist.", ephemeral: true).ConfigureAwait(false);
        }
    }

    [ComponentInteraction(PlaylistUiFactory.DashRemoveSelectId + ":*:*", true)]
    public async Task HandleDashboardRemoveSelect(long playlistId, int page, string[] selections)
    {
        await DeferAsync().ConfigureAwait(false);
        var playlist = await GetPlaylistByIdAsync(playlistId);
        if (playlist == null) return;

        var removedCount = 0;
        foreach (var posStr in selections)
        {
            if (!int.TryParse(posStr, out var position)) continue;
            var (success, _, _) =
                await playlistService.RemoveSongFromPlaylistAsync(Context.User.Id, Context.Guild.Id, playlist.Name,
                    position);
            if (success) removedCount++;
        }

        playlist = await GetPlaylistByIdAsync(playlistId);
        if (playlist == null) return;

        var components =
            PlaylistUiFactory.BuildPlaylistDetail(playlist, page, statusMessage: $"Removed {removedCount} song(s).");
        await ModifyOriginalResponseAsync(m =>
        {
            m.Components = components;
            m.Flags = MessageFlags.ComponentsV2;
        }).ConfigureAwait(false);
    }

    [ComponentInteraction(PlaylistUiFactory.DashShareSelectId + ":*", true)]
    public async Task HandleDashboardShareSelect(long playlistId, string[] userIds)
    {
        await DeferAsync().ConfigureAwait(false);
        var playlist = await GetPlaylistByIdAsync(playlistId);
        if (playlist == null) return;

        if (userIds.Length == 0 || !ulong.TryParse(userIds[0], out var targetUserId)) return;

        if (targetUserId == Context.User.Id)
        {
            await FollowupAsync("You cannot share a playlist with yourself.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var result = await playlistService.SharePlaylistAsync(Context.User.Id, Context.Guild.Id, playlist.Name,
            targetUserId);

        var components =
            PlaylistUiFactory.BuildPlaylistDetail(playlist, 1,
                statusMessage: result.Success ? "Playlist shared!" : result.Message);

        await ModifyOriginalResponseAsync(m =>
        {
            m.Components = components;
            m.Flags = MessageFlags.ComponentsV2;
        }).ConfigureAwait(false);

        if (result.Success)
            try
            {
                await Context.Channel.SendMessageAsync(
                    $"{Context.User.Mention} shared playlist '{playlist.Name}' with <@{targetUserId}>!",
                    allowedMentions: AllowedMentions.None);
            }
            catch
            {
                /* Ignore */
            }
    }

    [ModalInteraction("playlist:modal:create", true)]
    public async Task HandleCreateModal(CreatePlaylistModal modal)
    {
        await DeferAsync().ConfigureAwait(false);
        var result = await playlistService.CreatePlaylistAsync(Context.User.Id, Context.Guild.Id, modal.Name)
            .ConfigureAwait(false);

        if (result.Success)
        {
            var playlists = await playlistService.GetUserPlaylistsAsync(Context.User.Id, Context.Guild.Id)
                .ConfigureAwait(false);
            var components = PlaylistUiFactory.BuildPlaylistDashboard(playlists, Context.User.Id);
            await ModifyOriginalResponseAsync(m =>
            {
                m.Components = components;
                m.Flags = MessageFlags.ComponentsV2;
            }).ConfigureAwait(false);
            await FollowupAsync($"Playlist '{modal.Name}' created.", ephemeral: true).ConfigureAwait(false);
        }
        else
        {
            await FollowupAsync(result.Message, ephemeral: true).ConfigureAwait(false);
        }
    }

    [ModalInteraction("playlist:modal:rename:*", true)]
    public async Task HandleRenameModal(long playlistId, RenamePlaylistModal modal)
    {
        await DeferAsync().ConfigureAwait(false);
        var playlist = await GetPlaylistByIdAsync(playlistId);
        if (playlist == null) return;

        var result = await playlistService.RenamePlaylistAsync(Context.User.Id, Context.Guild.Id, playlist.Name,
            modal.NewName);

        if (result.Success)
        {
            playlist.Name = modal.NewName;
            var components = PlaylistUiFactory.BuildPlaylistDetail(playlist, 1, statusMessage: "Renamed successfully.");
            await ModifyOriginalResponseAsync(m =>
            {
                m.Components = components;
                m.Flags = MessageFlags.ComponentsV2;
            }).ConfigureAwait(false);
        }
        else
        {
            await FollowupAsync(result.Message, ephemeral: true).ConfigureAwait(false);
        }
    }

    [ModalInteraction("playlist:modal:add:*", true)]
    public async Task HandleAddSongModal(long playlistId, AddSongModal modal)
    {
        await DeferAsync().ConfigureAwait(false);
        var playlist = await GetPlaylistByIdAsync(playlistId);
        if (playlist == null) return;

        var (player, isError) =
            await GetVerifiedPlayerAsync(PlayerChannelBehavior.Join, MemberVoiceStateBehavior.RequireSame);
        if (isError || player == null) return;

        var result = await ProcessAddTrackLogic(player, playlist.Name, modal.Query);

        var updatedPlaylist = await GetPlaylistByIdAsync(playlistId);
        if (updatedPlaylist != null)
        {
            var components = PlaylistUiFactory.BuildPlaylistDetail(updatedPlaylist, 1,
                statusMessage: result.Success ? "Song(s) added!" : result.Message);
            await ModifyOriginalResponseAsync(m =>
            {
                m.Components = components;
                m.Flags = MessageFlags.ComponentsV2;
            }).ConfigureAwait(false);
        }
        else
        {
            await FollowupAsync(result.Message, ephemeral: !result.Success).ConfigureAwait(false);
        }
    }

    private async Task<PlaylistEntity?> GetPlaylistByIdAsync(long playlistId)
    {
        var playlist = await playlistService.GetPlaylistAsync(Context.User.Id, Context.Guild.Id, playlistId)
            .ConfigureAwait(false);

        if (playlist == null)
            await ModifyOriginalResponseAsync(m =>
            {
                m.Content = "Playlist not found or deleted.";
                m.Components = new ComponentBuilder().Build();
            }).ConfigureAwait(false);

        return playlist;
    }

    private async Task<(bool Success, string Message)> ProcessPlayPlaylistLogic(string playlistName, ulong userId)
    {
        var playlist = await playlistService.GetPlaylistAsync(userId, Context.Guild.Id, playlistName)
            .ConfigureAwait(false);
        if (playlist == null) return (false, "Playlist not found.");
        if (playlist.Items.Count == 0) return (false, "Playlist is empty.");

        var (player, isError) =
            await GetVerifiedPlayerAsync(PlayerChannelBehavior.Join, MemberVoiceStateBehavior.RequireSame)
                .ConfigureAwait(false);
        if (isError || player is null) return (false, "Could not connect to voice channel.");

        await player.Queue.ClearAsync().ConfigureAwait(false);
        var (addedCount, failedTracks) = await QueuePlaylistSongsAsync(player, playlist, userId).ConfigureAwait(false);

        if (addedCount == 0)
            return (false, "No songs could be loaded from the playlist.");

        await MusicService.StartPlaybackIfNeededAsync(player).ConfigureAwait(false);

        var msg = $"Playing **{addedCount}** song(s) from **'{playlistName}'**.";
        if (failedTracks.Count > 0)
            msg += $"\n(Failed to load {failedTracks.Count} songs)";

        return (true, msg);
    }

    private async Task<(bool Success, string Message)> ProcessAddTrackLogic(CustomPlayer player, string playlistName,
        string? query)
    {
        var tracksToAdd = new List<LavalinkTrack>();

        if (!string.IsNullOrWhiteSpace(query))
        {
            await MusicService.LoadAndQueueTrackAsync(player, query, Context.User)
                .ConfigureAwait(false);

            var resolutionScope = new LavalinkApiResolutionScope(player.ApiClient);
            var isUrl = Uri.TryCreate(query, UriKind.Absolute, out _);
            var searchMode = isUrl ? TrackSearchMode.None : TrackSearchMode.YouTube;
            var lavalinkResult = await audioService.Tracks.LoadTracksAsync(query, searchMode, resolutionScope)
                .ConfigureAwait(false);

            if (lavalinkResult.IsPlaylist)
                tracksToAdd.AddRange(lavalinkResult.Tracks);
            else if (lavalinkResult.Track != null)
                tracksToAdd.Add(lavalinkResult.Track);
            else if (!lavalinkResult.Tracks.IsEmpty)
                tracksToAdd.Add(lavalinkResult.Tracks.First());
            else
                return (false, "No tracks found.");
        }
        else if (player.CurrentTrack != null)
        {
            tracksToAdd.Add(player.CurrentTrack);
        }
        else
        {
            return (false, "Please provide a song/URL, or play a song to add the current one.");
        }

        var opResult = await playlistService.AddTracksToPlaylistAsync(Context.User.Id, Context.Guild.Id, playlistName,
            tracksToAdd).ConfigureAwait(false);
        return (opResult.Success, opResult.Message);
    }

    private async Task<(int AddedCount, List<string> FailedTracks)> QueuePlaylistSongsAsync(CustomPlayer player,
        PlaylistEntity playlist, ulong requesterId)
    {
        var addedCount = 0;
        var failedTracks = new List<string>();
        var resolutionScope = new LavalinkApiResolutionScope(player.ApiClient);

        var songs = playlist.Items.OrderBy(i => i.Position).Select(i => i.Track);

        foreach (var song in songs)
        {
            if (string.IsNullOrWhiteSpace(song.Uri))
            {
                failedTracks.Add(song.Title.Truncate(30));
                continue;
            }

            try
            {
                var trackLoadResult = await audioService.Tracks
                    .LoadTracksAsync(song.Uri, TrackSearchMode.None, resolutionScope)
                    .ConfigureAwait(false);
                if (trackLoadResult.Track != null)
                {
                    await player.Queue.AddAsync(new CustomTrackQueueItem(trackLoadResult.Track, requesterId))
                        .ConfigureAwait(false);
                    addedCount++;
                }
                else
                {
                    failedTracks.Add(song.Title.Truncate(30));
                }
            }
            catch
            {
                failedTracks.Add(song.Title.Truncate(30));
            }
        }

        return (addedCount, failedTracks);
    }

    public class CreatePlaylistModal : IModal
    {
        [ModalTextInput("name")] public string Name { get; set; } = string.Empty;
        public string Title => "Create Playlist";
    }

    public class RenamePlaylistModal : IModal
    {
        [ModalTextInput("new_name")] public string NewName { get; set; } = string.Empty;
        public string Title => "Rename Playlist";
    }

    public class AddSongModal : IModal
    {
        [ModalTextInput("query")] public string Query { get; set; } = string.Empty;
        public string Title => "Add Song";
    }
}