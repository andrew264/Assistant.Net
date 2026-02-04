using Assistant.Net.Options;
using Assistant.Net.Services.Data;
using Assistant.Net.Services.Music;
using Assistant.Net.Services.Music.Logic;
using Assistant.Net.Utilities;
using Assistant.Net.Utilities.Ui;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Lavalink4NET.Clients;
using Lavalink4NET.Players.Queued;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Net.Modules.Music.Interaction;

[CommandContextType(InteractionContextType.Guild)]
public class NowPlayingInteractionModule(
    NowPlayingService nowPlayingService,
    MusicService musicService,
    PlaylistService playlistService,
    IOptions<MusicOptions> musicOptions,
    ILogger<NowPlayingInteractionModule> logger) : MusicInteractionModuleBase(musicService, logger)
{
    [SlashCommand("nowplaying", "Displays the interactive Now Playing message.")]
    public async Task NowPlayingCommand()
    {
        var (player, isError) = await GetVerifiedPlayerAsync(memberBehavior: MemberVoiceStateBehavior.Ignore)
            .ConfigureAwait(false);

        if (isError || player == null || player.CurrentTrack == null)
        {
            if (!isError)
                await RespondAsync("I am not playing anything right now.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await nowPlayingService.RemoveNowPlayingMessageAsync(Context.Guild.Id).ConfigureAwait(false);

        var components =
            MusicUiFactory.BuildNowPlayingDisplay(player, Context.Guild.Id, musicOptions.Value.MaxPlayerVolumePercent);
        await RespondAsync(components: components).ConfigureAwait(false);
        var message = await GetOriginalResponseAsync().ConfigureAwait(false);

        nowPlayingService.TrackNowPlayingMessage(message, Context.Guild.Id);
    }

    [ComponentInteraction(NowPlayingService.NpCustomIdPrefix + ":*:*", true)]
    public async Task HandleNowPlayingInteraction(ulong guildIdParam, string action)
    {
        if (Context.Guild == null || Context.Guild.Id != guildIdParam)
        {
            await RespondAsync("Button interaction guild mismatch. This shouldn't happen.", ephemeral: true)
                .ConfigureAwait(false);
            Logger.LogWarning(
                "NP Interaction guild mismatch. Context Guild: {ContextGuildId}, Param Guild: {ParamGuildId}, Action: {Action}, User: {UserId}",
                Context.Guild?.Id, guildIdParam, action, Context.User.Id);
            return;
        }

        await DeferAsync().ConfigureAwait(false);

        var (player, isError) = await GetVerifiedPlayerAsync(memberBehavior: MemberVoiceStateBehavior.RequireSame)
            .ConfigureAwait(false);

        if (isError || player == null)
        {
            if (!isError)
                await FollowupAsync(MusicModuleHelpers.GetPlayerRetrieveErrorMessage(0), ephemeral: true)
                    .ConfigureAwait(false);
            await nowPlayingService.RemoveNowPlayingMessageAsync(guildIdParam).ConfigureAwait(false);
            return;
        }

        string? ephemeralFollowupMessage = null;

        switch (action)
        {
            case "add_to_playlist":
                var playlists = await playlistService.GetUserPlaylistsAsync(Context.User.Id, Context.Guild.Id)
                    .ConfigureAwait(false);

                var components =
                    MusicUiFactory.BuildAddToPlaylistMenu(playlists, player.CurrentTrack?.Title ?? "Unknown");

                await FollowupAsync(components: components, ephemeral: true, flags: MessageFlags.ComponentsV2)
                    .ConfigureAwait(false);
                return;

            case "prev_restart":
                if (player.CurrentTrack != null)
                {
                    await player.SeekAsync(TimeSpan.Zero).ConfigureAwait(false);
                    Logger.LogInformation("[NP Button] User {User} restarted track in Guild {GuildId}",
                        Context.User.Username, guildIdParam);
                }

                break;
            case "pause_resume":
                await MusicService.PauseOrResumeAsync(player, Context.User).ConfigureAwait(false);
                break;
            case "skip":
                await MusicService.SkipTrackAsync(player, Context.User).ConfigureAwait(false);
                break;
            case "stop":
                await MusicService.StopPlaybackAsync(player, Context.User).ConfigureAwait(false);
                await FollowupAsync("Playback stopped and queue cleared.", ephemeral: true).ConfigureAwait(false);
                return;
            case "loop":
                TrackRepeatMode newMode;
                switch (player.RepeatMode)
                {
                    case TrackRepeatMode.None:
                        newMode = TrackRepeatMode.Track;
                        ephemeralFollowupMessage =
                            $"🔂 Looping current track: {player.CurrentTrack?.Title.Truncate(50)}";
                        break;
                    case TrackRepeatMode.Track:
                        newMode = TrackRepeatMode.Queue;
                        var songCount = player.Queue.Count + (player.CurrentTrack != null ? 1 : 0);
                        ephemeralFollowupMessage = $"🔁 Looping queue ({songCount} songs)";
                        break;
                    case TrackRepeatMode.Queue:
                    default:
                        newMode = TrackRepeatMode.None;
                        ephemeralFollowupMessage = "➡️ Loop disabled";
                        break;
                }

                player.RepeatMode = newMode;
                Logger.LogInformation("[NP Button] User {User} set loop mode to {LoopMode} in Guild {GuildId}",
                    Context.User.Username, newMode, guildIdParam);
                break;
            case "vol_down":
                var currentVolDown = player.Volume;
                var newVolDown = Math.Max(0f, currentVolDown - 0.10f);
                await MusicService.SetVolumeAsync(player, Context.User, (int)(newVolDown * 100)).ConfigureAwait(false);
                break;
            case "vol_up":
                var currentVolUp = player.Volume;
                var newVolUp = Math.Min(musicOptions.Value.MaxPlayerVolumePercent / 100f, currentVolUp + 0.10f);
                await MusicService.SetVolumeAsync(player, Context.User, (int)(newVolUp * 100)).ConfigureAwait(false);
                break;
            default:
                Logger.LogWarning("Unhandled NP action: {Action} for Guild {GuildId} by User {User}", action,
                    guildIdParam, Context.User.Id);
                await FollowupAsync("Unknown action.", ephemeral: true).ConfigureAwait(false);
                return;
        }

        await nowPlayingService.UpdateNowPlayingMessageAsync(guildIdParam, player).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(ephemeralFollowupMessage))
            await FollowupAsync(ephemeralFollowupMessage, ephemeral: true).ConfigureAwait(false);
    }

    [ComponentInteraction("np:playlist:select")]
    public async Task HandleAddToPlaylistSelect(string[] selection)
    {
        if (selection.Length == 0) return;

        await DeferAsync(true).ConfigureAwait(false);

        var (player, isError) = await GetVerifiedPlayerAsync(memberBehavior: MemberVoiceStateBehavior.Ignore)
            .ConfigureAwait(false);

        if (isError || player?.CurrentTrack == null)
        {
            await FollowupAsync("Something went wrong or nothing is playing.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (!long.TryParse(selection[0], out var playlistId)) return;

        var playlist = await playlistService.GetPlaylistAsync(Context.User.Id, Context.Guild.Id, playlistId)
            .ConfigureAwait(false);

        if (playlist == null)
        {
            await FollowupAsync("Playlist not found.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var result = await playlistService
            .AddTracksToPlaylistAsync(Context.User.Id, Context.Guild.Id, playlist.Name, [player.CurrentTrack])
            .ConfigureAwait(false);

        if (result.Success)
        {
            var components = MusicUiFactory.BuildAddToPlaylistSuccess(player.CurrentTrack.Title, playlist.Name);
            await ModifyOriginalResponseAsync(msg =>
            {
                msg.Content = "";
                msg.Components = components;
                msg.Flags = MessageFlags.ComponentsV2;
            }).ConfigureAwait(false);
        }
        else
        {
            await FollowupAsync(result.Message, ephemeral: true).ConfigureAwait(false);
        }
    }

    [ComponentInteraction("np:playlist:create")]
    public async Task HandleAddToPlaylistCreate()
    {
        var modal = new ModalBuilder()
            .WithTitle("Create Playlist")
            .WithCustomId("np:modal:create")
            .AddTextInput("Playlist Name", "name", placeholder: "My Awesome Playlist", maxLength: 100)
            .Build();
        await RespondWithModalAsync(modal).ConfigureAwait(false);
    }

    [ModalInteraction("np:modal:create")]
    public async Task HandleCreatePlaylistModal(PlaylistInteractionModule.CreatePlaylistModal modal)
    {
        var result = await playlistService.CreatePlaylistAsync(Context.User.Id, Context.Guild.Id, modal.Name)
            .ConfigureAwait(false);
        if (Context.Interaction is not SocketModal modalInteraction || !result.Success)
        {
            await RespondAsync(result.Message, ephemeral: true).ConfigureAwait(false);
            return;
        }

        var (player, _) = await GetVerifiedPlayerAsync(memberBehavior: MemberVoiceStateBehavior.Ignore)
            .ConfigureAwait(false);
        var songTitle = player?.CurrentTrack?.Title ?? "Unknown Song";

        var playlists = await playlistService.GetUserPlaylistsAsync(Context.User.Id, Context.Guild.Id)
            .ConfigureAwait(false);

        var components = MusicUiFactory.BuildAddToPlaylistMenu(playlists, songTitle);

        await modalInteraction.UpdateAsync(msg =>
        {
            msg.Components = components;
            msg.Flags = MessageFlags.ComponentsV2;
        }).ConfigureAwait(false);
    }
}