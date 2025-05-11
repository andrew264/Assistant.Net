using Assistant.Net.Configuration;
using Assistant.Net.Modules.Music.Helpers;
using Assistant.Net.Services;
using Assistant.Net.Utilities;
using Discord;
using Discord.Commands;
using Discord.Interactions;
using Lavalink4NET.Clients;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Music.NowPlaying;

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
        await DeferAsync(true);

        var (player, _) = await musicService.GetPlayerForContextAsync(
            Context.Guild, Context.User, Context.Channel,
            PlayerChannelBehavior.None, MemberVoiceStateBehavior.Ignore);

        if (player == null || player.CurrentTrack == null)
        {
            await FollowupAsync("I am not playing anything right now.", ephemeral: true);
            return;
        }

        var npMessage = await nowPlayingService.CreateOrReplaceNowPlayingMessageAsync(player, Context);
        if (npMessage != null)
            await FollowupAsync("Now Playing message created/updated!", ephemeral: true);
        else
            await FollowupAsync("Failed to create or update the Now Playing message.", ephemeral: true);
    }

    [ComponentInteraction(NowPlayingService.NpCustomIdPrefix + ":*:*", true)]
    public async Task HandleNowPlayingInteraction(ulong guildIdParam, string action)
    {
        if (Context.Guild == null || Context.Guild.Id != guildIdParam)
        {
            await RespondAsync("Button interaction guild mismatch. This shouldn't happen.", ephemeral: true);
            logger.LogWarning(
                "NP Interaction guild mismatch. Context Guild: {ContextGuildId}, Param Guild: {ParamGuildId}, Action: {Action}, User: {UserId}",
                Context.Guild?.Id, guildIdParam, action, Context.User.Id);
            return;
        }

        await DeferAsync();

        var (player, retrieveStatus) = await musicService.GetPlayerForContextAsync(
            Context.Guild, Context.User, Context.Channel,
            PlayerChannelBehavior.None, MemberVoiceStateBehavior.RequireSame);

        if (player == null)
        {
            await FollowupAsync(MusicModuleHelpers.GetPlayerRetrieveErrorMessage(retrieveStatus), ephemeral: true);
            await nowPlayingService.RemoveNowPlayingMessageAsync(guildIdParam);
            return;
        }

        string? ephemeralFollowupMessage = null;

        switch (action)
        {
            case "prev_restart":
                if (player.CurrentTrack != null)
                {
                    await player.SeekAsync(TimeSpan.Zero);
                    logger.LogInformation("[NP Button] User {User} restarted track in Guild {GuildId}",
                        Context.User.Username, guildIdParam);
                }

                break;
            case "rewind":
                if (player.CurrentTrack != null && player.Position != null)
                {
                    var newPosition = player.Position.Value.Position - TimeSpan.FromSeconds(10);
                    if (newPosition < TimeSpan.Zero) newPosition = TimeSpan.Zero;
                    await player.SeekAsync(newPosition);
                    logger.LogInformation("[NP Button] User {User} rewound track in Guild {GuildId}",
                        Context.User.Username, guildIdParam);
                }

                break;
            case "pause_resume":
                await musicService.PauseOrResumeAsync(player, Context.User);
                break;
            case "forward":
                if (player.CurrentTrack != null && player.Position != null)
                {
                    if (player.Position.Value.Position < player.CurrentTrack.Duration - TimeSpan.FromSeconds(10))
                        await player.SeekAsync(player.Position.Value.Position + TimeSpan.FromSeconds(10));
                    else
                        await musicService.SkipTrackAsync(player, Context.User);
                    logger.LogInformation("[NP Button] User {User} forwarded track in Guild {GuildId}",
                        Context.User.Username, guildIdParam);
                }

                break;
            case "skip":
                await musicService.SkipTrackAsync(player, Context.User);
                break;
            case "stop":
                await musicService.StopPlaybackAsync(player, Context.User);
                await FollowupAsync("Playback stopped and queue cleared.", ephemeral: true);
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
                await musicService.SetVolumeAsync(player, Context.User, (int)(newVolDown * 100));
                break;
            case "vol_up":
                var currentVolUp = player.Volume;
                var newVolUp = Math.Min(config.Music.MaxPlayerVolumePercent / 100f, currentVolUp + 0.10f);
                await musicService.SetVolumeAsync(player, Context.User, (int)(newVolUp * 100));
                break;
            default:
                logger.LogWarning("Unhandled NP action: {Action} for Guild {GuildId} by User {User}", action,
                    guildIdParam, Context.User.Id);
                await FollowupAsync("Unknown action.", ephemeral: true);
                return;
        }

        await nowPlayingService.UpdateNowPlayingMessageAsync(guildIdParam, player);

        if (!string.IsNullOrEmpty(ephemeralFollowupMessage))
            await FollowupAsync(ephemeralFollowupMessage, ephemeral: true);
    }
}