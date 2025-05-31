using Assistant.Net.Configuration;
using Assistant.Net.Modules.Music.Logic;
using Assistant.Net.Services.Music;
using Assistant.Net.Utilities;
using Discord;
using Discord.Commands;
using Discord.Interactions;
using Lavalink4NET.Clients;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Music.InteractionModules;

[CommandContextType(InteractionContextType.Guild)]
public class NowPlayingInteractionModule(
    NowPlayingService nowPlayingService,
    MusicService musicService,
    Config config,
    ILogger<NowPlayingInteractionModule> logger)
    : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("nowplaying", "Displays the interactive Now Playing message.")]
    [Alias("np")]
    public async Task NowPlayingCommand()
    {
        await DeferAsync(true).ConfigureAwait(false);

        var (player, _) = await musicService.GetPlayerForContextAsync(
            Context.Guild, Context.User, Context.Channel,
            PlayerChannelBehavior.None, MemberVoiceStateBehavior.Ignore).ConfigureAwait(false);

        if (player == null || player.CurrentTrack == null)
        {
            await FollowupAsync("I am not playing anything right now.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var npMessage = await nowPlayingService.CreateOrReplaceNowPlayingMessageAsync(player, Context)
            .ConfigureAwait(false);
        if (npMessage != null)
            await FollowupAsync("Now Playing message created/updated!", ephemeral: true).ConfigureAwait(false);
        else
            await FollowupAsync("Failed to create or update the Now Playing message.", ephemeral: true)
                .ConfigureAwait(false);
    }

    [ComponentInteraction(NowPlayingService.NpCustomIdPrefix + ":*:*", true)]
    public async Task HandleNowPlayingInteraction(ulong guildIdParam, string action)
    {
        if (Context.Guild == null || Context.Guild.Id != guildIdParam)
        {
            await RespondAsync("Button interaction guild mismatch. This shouldn't happen.", ephemeral: true)
                .ConfigureAwait(false);
            logger.LogWarning(
                "NP Interaction guild mismatch. Context Guild: {ContextGuildId}, Param Guild: {ParamGuildId}, Action: {Action}, User: {UserId}",
                Context.Guild?.Id, guildIdParam, action, Context.User.Id);
            return;
        }

        await DeferAsync().ConfigureAwait(false);

        var (player, retrieveStatus) = await musicService.GetPlayerForContextAsync(
            Context.Guild, Context.User, Context.Channel,
            PlayerChannelBehavior.None, MemberVoiceStateBehavior.RequireSame).ConfigureAwait(false);

        if (player == null)
        {
            await FollowupAsync(MusicModuleHelpers.GetPlayerRetrieveErrorMessage(retrieveStatus), ephemeral: true)
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
                    logger.LogInformation("[NP Button] User {User} restarted track in Guild {GuildId}",
                        Context.User.Username, guildIdParam);
                }

                break;
            case "rewind":
                if (player.CurrentTrack != null && player.Position != null)
                {
                    var newPosition = player.Position.Value.Position - TimeSpan.FromSeconds(10);
                    if (newPosition < TimeSpan.Zero) newPosition = TimeSpan.Zero;
                    await player.SeekAsync(newPosition).ConfigureAwait(false);
                    logger.LogInformation("[NP Button] User {User} rewound track in Guild {GuildId}",
                        Context.User.Username, guildIdParam);
                }

                break;
            case "pause_resume":
                await musicService.PauseOrResumeAsync(player, Context.User).ConfigureAwait(false);
                break;
            case "forward":
                if (player.CurrentTrack != null && player.Position != null)
                {
                    if (player.Position.Value.Position < player.CurrentTrack.Duration - TimeSpan.FromSeconds(10))
                        await player.SeekAsync(player.Position.Value.Position + TimeSpan.FromSeconds(10))
                            .ConfigureAwait(false);
                    else
                        await musicService.SkipTrackAsync(player, Context.User).ConfigureAwait(false);
                    logger.LogInformation("[NP Button] User {User} forwarded track in Guild {GuildId}",
                        Context.User.Username, guildIdParam);
                }

                break;
            case "skip":
                await musicService.SkipTrackAsync(player, Context.User).ConfigureAwait(false);
                break;
            case "stop":
                await musicService.StopPlaybackAsync(player, Context.User).ConfigureAwait(false);
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
                logger.LogInformation("[NP Button] User {User} set loop mode to {LoopMode} in Guild {GuildId}",
                    Context.User.Username, newMode, guildIdParam);
                break;
            case "vol_down":
                var currentVolDown = player.Volume;
                var newVolDown = Math.Max(0f, currentVolDown - 0.10f);
                await musicService.SetVolumeAsync(player, Context.User, (int)(newVolDown * 100)).ConfigureAwait(false);
                break;
            case "vol_up":
                var currentVolUp = player.Volume;
                var newVolUp = Math.Min(config.Music.MaxPlayerVolumePercent / 100f, currentVolUp + 0.10f);
                await musicService.SetVolumeAsync(player, Context.User, (int)(newVolUp * 100)).ConfigureAwait(false);
                break;
            default:
                logger.LogWarning("Unhandled NP action: {Action} for Guild {GuildId} by User {User}", action,
                    guildIdParam, Context.User.Id);
                await FollowupAsync("Unknown action.", ephemeral: true).ConfigureAwait(false);
                return;
        }

        await nowPlayingService.UpdateNowPlayingMessageAsync(guildIdParam, player).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(ephemeralFollowupMessage))
            await FollowupAsync(ephemeralFollowupMessage, ephemeral: true).ConfigureAwait(false);
    }
}