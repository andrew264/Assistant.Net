using Assistant.Net.Configuration;
using Assistant.Net.Services.Music;
using Assistant.Net.Services.Music.Logic;
using Assistant.Net.Utilities;
using Assistant.Net.Utilities.Ui;
using Discord;
using Discord.Interactions;
using Lavalink4NET.Clients;
using Lavalink4NET.Players.Queued;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Music.Interaction;

[CommandContextType(InteractionContextType.Guild)]
public class NowPlayingInteractionModule(
    NowPlayingService nowPlayingService,
    MusicService musicService,
    Config config,
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

        var components = MusicUiFactory.BuildNowPlayingDisplay(player, Context.Guild.Id, config);
        await RespondAsync(components: components).ConfigureAwait(false);
        var message = await GetOriginalResponseAsync().ConfigureAwait(false);

        nowPlayingService.TrackNowPlayingMessage(message, Context.User, Context.Guild.Id);
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
            case "prev_restart":
                if (player.CurrentTrack != null)
                {
                    await player.SeekAsync(TimeSpan.Zero).ConfigureAwait(false);
                    Logger.LogInformation("[NP Button] User {User} restarted track in Guild {GuildId}",
                        Context.User.Username, guildIdParam);
                }

                break;
            case "rewind":
                if (player.CurrentTrack != null && player.Position != null)
                {
                    var newPosition = player.Position.Value.Position - TimeSpan.FromSeconds(10);
                    if (newPosition < TimeSpan.Zero) newPosition = TimeSpan.Zero;
                    await player.SeekAsync(newPosition).ConfigureAwait(false);
                    Logger.LogInformation("[NP Button] User {User} rewound track in Guild {GuildId}",
                        Context.User.Username, guildIdParam);
                }

                break;
            case "pause_resume":
                await MusicService.PauseOrResumeAsync(player, Context.User).ConfigureAwait(false);
                break;
            case "forward":
                if (player.CurrentTrack != null && player.Position != null)
                {
                    if (player.Position.Value.Position < player.CurrentTrack.Duration - TimeSpan.FromSeconds(10))
                        await player.SeekAsync(player.Position.Value.Position + TimeSpan.FromSeconds(10))
                            .ConfigureAwait(false);
                    else
                        await MusicService.SkipTrackAsync(player, Context.User).ConfigureAwait(false);
                    Logger.LogInformation("[NP Button] User {User} forwarded track in Guild {GuildId}",
                        Context.User.Username, guildIdParam);
                }

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
                var newVolUp = Math.Min(config.Music.MaxPlayerVolumePercent / 100f, currentVolUp + 0.10f);
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
}