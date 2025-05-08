using Assistant.Net.Configuration;
using Assistant.Net.Modules.Music.Player;
using Assistant.Net.Services;
using Assistant.Net.Utilities;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Lavalink4NET;
using Lavalink4NET.Clients;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Music.Commands;

public class CommandsModule(
    IAudioService audioService,
    ILogger<CommandsModule> logger,
    MusicHistoryService musicHistoryService,
    Config config) : ModuleBase<SocketCommandContext>
{
    private static string Clickable(string title, Uri? uri) => $"[{title}](<{uri?.AbsoluteUri}>)";


    [Command("skip")]
    [Alias("s")]
    [Summary("Skip songs that are in queue")]
    public async Task SkipPrefixAsync([Summary("The index of the song in the queue to skip.")] int index = 0)
    {
        var player = await GetPlayerAsyncPrefix(false);
        if (player?.CurrentTrack is null)
        {
            await ReplyAsync("I am not playing anything right now.");
            return;
        }

        if (index < 0) index = player.Queue.Count + index + 1;

        if (index == 0)
        {
            var currentTrack = player.CurrentTrack;
            await player.SkipAsync();
            await ReplyAsync($"Skipping {Clickable(currentTrack.Title, currentTrack.Uri)}");
            logger.LogInformation("[Player:{GuildId}] Skipped current track '{TrackTitle}' by {User} (Prefix)",
                player.GuildId, currentTrack.Title, Context.User);
            return;
        }

        if (index > player.Queue.Count)
        {
            await ReplyAsync("Invalid index. The queue is not that long.");
            return;
        }

        var queuedTrack = player.Queue[index - 1];
        if (queuedTrack.Track is null)
        {
            await ReplyAsync("Invalid track at the specified index.");
            return;
        }

        await ReplyAsync($"Skipping {Clickable(queuedTrack.Track.Title, queuedTrack.Track.Uri)} from queue.");
        await player.Queue.RemoveAtAsync(index - 1);
        logger.LogInformation(
            "[Player:{GuildId}] Removed track '{TrackTitle}' from queue at index {Index} by {User} (Prefix)",
            player.GuildId, queuedTrack.Track.Title, index, Context.User);
    }

    [Command("loop")]
    [Alias("l")]
    [Summary("Toggles looping for the current song or the entire queue.")]
    public async Task LoopPrefixAsync()
    {
        var player = await GetPlayerAsyncPrefix(false);
        if (player?.CurrentTrack is null)
        {
            await ReplyAsync("I am not playing anything right now.");
            return;
        }

        string replyMessage;
        switch (player.RepeatMode)
        {
            case TrackRepeatMode.None:
                player.RepeatMode = TrackRepeatMode.Track;
                replyMessage = $"Looping {Clickable(player.CurrentTrack.Title, player.CurrentTrack.Uri)}.";
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
                await ReplyAsync("An unexpected error occurred with the loop mode.");
                return;
        }

        await ReplyAsync(replyMessage);
        logger.LogInformation("[Player:{GuildId}] Loop mode set to {RepeatMode} by {User} (Prefix)", player.GuildId,
            player.RepeatMode, Context.User);
    }

    [Command("volume")]
    [Alias("vol")]
    [Summary("Sets or displays the player volume (0-200%).")]
    public async Task VolumePrefixAsync([Summary("The volume to set (0-200).")] int? volume = null)
    {
        var player = await GetPlayerAsyncPrefix(false);
        if (player is null)
        {
            await ReplyAsync("I am not connected to a voice channel.");
            return;
        }

        switch (volume)
        {
            case null:
                await ReplyAsync($"Current Volume: {(int)(player.Volume * 100)}%");
                return;
            case >= 0 and <= 200:
                await player.SetVolumeAsync(volume.Value / 100f);
                await ReplyAsync($"Volume set to `{volume}%`");
                await musicHistoryService.SetGuildVolumeAsync(Context.Guild.Id, volume.Value / 100f);
                logger.LogInformation("[Player:{GuildId}] Volume set to {Volume}% by {User} (Prefix)", player.GuildId,
                    volume, Context.User);
                break;
            default:
                await ReplyAsync("Volume out of range. Please use a value between 0 and 200.");
                break;
        }
    }

    [Command("stop")]
    [Alias("leave", "disconnect", "dc")]
    [Summary("Stops the music, clears the queue, and disconnects the bot.")]
    public async Task StopPrefixAsync()
    {
        var player = await GetPlayerAsyncPrefix(false);
        if (player is null)
        {
            await ReplyAsync("I am not playing anything right now.");
            return;
        }

        await player.Queue.ClearAsync();
        await player.StopAsync();
        await ReplyAsync("Thanks for Listening.");
        await Context.Message.AddReactionAsync(new Emoji("👋"));
        logger.LogInformation("[Player:{GuildId}] Stopped and disconnected by {User} (Prefix)", player.GuildId,
            Context.User);
    }

    [Command("skipto")]
    [Alias("st")]
    [Summary("Skips to a specific song in the queue.")]
    public async Task SkipToPrefixAsync([Summary("The index of the song to skip to.")] int index = 0)
    {
        var player = await GetPlayerAsyncPrefix(false);
        if (player?.CurrentTrack is null)
        {
            await ReplyAsync("I am not playing anything right now.");
            return;
        }

        if (index < 0) index = player.Queue.Count + index + 1;
        var currentTrack = player.CurrentTrack;

        if (index == 0)
        {
            await player.SeekAsync(TimeSpan.Zero);
            await ReplyAsync($"Restarting {Clickable(currentTrack.Title, currentTrack.Uri)}.");
            logger.LogInformation("[Player:{GuildId}] Restarted track '{TrackTitle}' by {User} (Prefix)",
                player.GuildId, currentTrack.Title, Context.User);
            return;
        }

        if (index > player.Queue.Count)
        {
            await ReplyAsync("Invalid index. The queue is not that long.");
            return;
        }

        var trackToPlay = player.Queue[index - 1];
        if (trackToPlay.Track is null)
        {
            await ReplyAsync("Invalid track at the specified index.");
            return;
        }

        await ReplyAsync($"Skipping to {Clickable(trackToPlay.Track.Title, trackToPlay.Track.Uri)}.");
        await player.Queue.RemoveAtAsync(index - 1);
        await player.Queue.InsertAsync(0, new TrackQueueItem(trackToPlay.Track));
        await player.Queue.InsertAsync(index, new TrackQueueItem(currentTrack));
        await player.SkipAsync();
        logger.LogInformation(
            "[Player:{GuildId}] Skipped to track '{TrackTitle}' (from index {Index}) by {User} (Prefix)",
            player.GuildId, trackToPlay.Track.Title, index, Context.User);
    }

    [Command("seek")]
    [Summary("Seeks to a specific time in the current song.")]
    public async Task SeekPrefixAsync([Summary("Time to seek to in MM:SS format")] string time = "0")
    {
        var player = await GetPlayerAsyncPrefix(false);
        if (player?.CurrentTrack is null)
        {
            await ReplyAsync("I am not playing anything right now.");
            return;
        }

        var timeSpan = TimeUtils.ParseTimestamp(time);
        if (timeSpan > player.CurrentTrack.Duration)
        {
            await ReplyAsync($"Cannot seek beyond the song's duration ({player.CurrentTrack.Duration:mm\\:ss}).");
            return;
        }

        if (timeSpan < TimeSpan.Zero)
        {
            await ReplyAsync("Cannot seek to a negative time.");
            return;
        }

        await player.SeekAsync(timeSpan);
        await ReplyAsync(
            $"Seeked {Clickable(player.CurrentTrack.Title, player.CurrentTrack.Uri)} to `{timeSpan:mm\\:ss}`.");
        logger.LogInformation("[Player:{GuildId}] Seeked track '{TrackTitle}' to {Time} by {User} (Prefix)",
            player.GuildId, player.CurrentTrack.Title, timeSpan, Context.User);
    }

    private async ValueTask<CustomPlayer?> GetPlayerAsyncPrefix(bool connectToVoiceChannel = true)
    {
        if (Context.User is not SocketGuildUser guildUser)
        {
            await ReplyAsync("You must be in a guild to use this command.");
            return null;
        }

        var voiceChannelId = guildUser.VoiceChannel?.Id;
        if (connectToVoiceChannel && voiceChannelId is null)
        {
            await ReplyAsync("You must be connected to a voice channel to use this command.");
            return null;
        }

        var retrieveOptions = new PlayerRetrieveOptions(
            connectToVoiceChannel ? PlayerChannelBehavior.Join : PlayerChannelBehavior.None,
            connectToVoiceChannel ? MemberVoiceStateBehavior.RequireSame : MemberVoiceStateBehavior.Ignore
        );

        var playerOptions = new CustomPlayerOptions
        {
            TextChannel = Context.Channel as ITextChannel ??
                          throw new InvalidOperationException("Command invoked outside a valid text channel."),
            SocketClient = Context.Client,
            ApplicationConfig = config,
            InitialVolume = await musicHistoryService.GetGuildVolumeAsync(Context.Guild.Id)
        };

        var result = await audioService.Players.RetrieveAsync<CustomPlayer, CustomPlayerOptions>(
            Context.Guild.Id, voiceChannelId,
            static (props, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return ValueTask.FromResult(new CustomPlayer(props));
            },
            playerOptions,
            retrieveOptions);

        if (result.IsSuccess) return result.Player;

        var errorMessage = result.Status switch
        {
            PlayerRetrieveStatus.UserNotInVoiceChannel => "You are not connected to a voice channel.",
            PlayerRetrieveStatus.BotNotConnected => "The bot is currently not connected to a voice channel.",
            PlayerRetrieveStatus.VoiceChannelMismatch => "You must be in the same voice channel as the bot.",
            PlayerRetrieveStatus.PreconditionFailed => "The bot is already connected to a different voice channel.",
            _ => "An unknown error occurred while retrieving the player."
        };

        logger.LogWarning(
            "Failed to retrieve player for Guild {GuildId} by User {UserId}. Status: {Status} (Prefix Command)",
            Context.Guild.Id, Context.User.Id, result.Status);

        await ReplyAsync(errorMessage);
        return null;
    }
}