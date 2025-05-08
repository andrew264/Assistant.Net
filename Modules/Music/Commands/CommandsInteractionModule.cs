using Assistant.Net.Configuration;
using Assistant.Net.Modules.Music.PlayCommand;
using Assistant.Net.Modules.Music.Player;
using Assistant.Net.Services;
using Assistant.Net.Utilities;
using Discord;
using Discord.Interactions;
using Lavalink4NET;
using Lavalink4NET.Clients;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Music.Commands;

[CommandContextType(InteractionContextType.Guild)]
public class CommandsInteractionModule(
    IAudioService audioService,
    ILogger<PlayInteractionModule> logger,
    MusicHistoryService musicHistoryService,
    Config config)
    : InteractionModuleBase<SocketInteractionContext>
{
    private static string Clickable(string title, Uri? uri) => $"[{title}](<{uri?.AbsoluteUri}>)";

    [SlashCommand("skip", "Skip songs that are in queue")]
    public async Task Skip([Summary(description: "Index of the song to skip")] int index = 0)
    {
        var player = await GetPlayerAsync(false);
        if (player?.CurrentTrack is null)
        {
            await RespondOrFollowupAsync("I am not playing anything right now.", true);
            return;
        }

        await DeferAsync();
        if (index < 0) index = player.Queue.Count + index + 1;

        if (index == 0)
        {
            var currentTrack = player.CurrentTrack;
            await player.SkipAsync();
            await RespondOrFollowupAsync($"Skipping {Clickable(currentTrack.Title, currentTrack.Uri)}");
            return;
        }

        if (index > player.Queue.Count)
        {
            await RespondOrFollowupAsync("Invalid index.", true);
            return;
        }

        var queuedTrack = player.Queue[index - 1];
        if (queuedTrack.Track is null)
        {
            await RespondOrFollowupAsync("Invalid index.", true);
            return;
        }

        await RespondOrFollowupAsync($"Skipping {Clickable(queuedTrack.Track.Title, queuedTrack.Track.Uri)}");
        await player.Queue.RemoveAtAsync(index - 1);
    }

    [SlashCommand("loop", "Loops the current song or the entire queue.")]
    public async Task Loop()
    {
        var player = await GetPlayerAsync(false);
        if (player?.CurrentTrack is null)
        {
            await RespondOrFollowupAsync("I am not playing anything right now.", true);
            return;
        }

        await DeferAsync();
        switch (player.RepeatMode)
        {
            case TrackRepeatMode.None:
                player.RepeatMode = TrackRepeatMode.Track;
                await RespondOrFollowupAsync(
                    $"Looping {Clickable(player.CurrentTrack.Title, player.CurrentTrack.Uri)}.");
                break;
            case TrackRepeatMode.Track:
                player.RepeatMode = TrackRepeatMode.Queue;
                await RespondOrFollowupAsync($"Looping all {player.Queue.Count} tracks in the queue.");
                break;
            case TrackRepeatMode.Queue:
                player.RepeatMode = TrackRepeatMode.None;
                await RespondOrFollowupAsync("Stopped looping.");
                break;
            default:
                throw new ArgumentOutOfRangeException(); // should not happen
        }
    }


    [SlashCommand("volume", "Sets the player volume (0 - 200%)")]
    public async Task Volume([Summary(description: "Volume to set [0 - 200] %")] int? volume = null)
    {
        var player = await GetPlayerAsync(false);
        if (player is null)
        {
            await RespondOrFollowupAsync("I am not playing anything right now.", true);
            return;
        }

        await DeferAsync();
        switch (volume)
        {
            case null:
                await RespondOrFollowupAsync($"Current Volume: {(int)(player.Volume * 100)}%");
                return;
            case > 0 and < 201:
                await player.SetVolumeAsync((float)(volume / 100f));
                await RespondOrFollowupAsync($"Volume set to `{volume}%`");
                break;
            default:
                await RespondOrFollowupAsync("Volume out of range: 0% - 200%!");
                return;
        }
    }

    [SlashCommand("stop", "Stops the music and disconnects the bot from the voice channel")]
    public async Task Stop()
    {
        var player = await GetPlayerAsync(false);
        if (player is null)
        {
            await RespondOrFollowupAsync("I am not playing anything right now.", true);
            return;
        }

        await DeferAsync();

        await player.Queue.ClearAsync();
        await player.StopAsync();
        await player.DisconnectAsync();
        await RespondOrFollowupAsync("Thanks for Listening");
    }

    [SlashCommand("skipto", "Skip to a specific song in the queue")]
    public async Task SkipTo([Summary(description: "Index of the song to skip to")] int index = 0)
    {
        var player = await GetPlayerAsync(false);
        if (player?.CurrentTrack is null)
        {
            await RespondOrFollowupAsync("I am not playing anything right now.", true);
            return;
        }

        await DeferAsync();

        if (index < 0) index = player.Queue.Count + index + 1;

        if (index == 0)
        {
            var currentTrack = player.CurrentTrack;
            await player.SeekAsync(TimeSpan.FromSeconds(0));
            await RespondOrFollowupAsync($"Skipping  to {Clickable(currentTrack.Title, currentTrack.Uri)}");
        }
        else if (index > player.Queue.Count)
        {
            await RespondOrFollowupAsync("Invalid index.", true);
        }
        else
        {
            var queuedTrack = player.Queue[index - 1].Track;
            var currentTrack = player.CurrentTrack;
            if (queuedTrack is null)
            {
                await RespondOrFollowupAsync("Invalid index.", true);
                return;
            }

            await RespondOrFollowupAsync($"Skipping  to {Clickable(queuedTrack.Title, queuedTrack.Uri)}");
            await player.Queue.RemoveAtAsync(index - 1);
            await player.Queue.InsertAsync(0, new TrackQueueItem(queuedTrack));
            await player.Queue.InsertAsync(index, new TrackQueueItem(currentTrack));
            await player.SkipAsync();
        }
    }

    [SlashCommand("seek", "Seek to a specific time in the song")]
    public async Task Seek([Summary(description: "Time to seek to in MM:SS format")] string time = "0")
    {
        var player = await GetPlayerAsync(false);
        if (player?.CurrentTrack is null)
        {
            await RespondOrFollowupAsync("I am not playing anything right now.", true);
            return;
        }

        await DeferAsync();

        var currentTrack = player.CurrentTrack;
        var timeSpan = TimeUtils.ParseTimestamp(time);
        await player.SeekAsync(timeSpan);
        await RespondOrFollowupAsync($"Seeked {Clickable(currentTrack.Title, currentTrack.Uri)} to `{time}`");
    }

    private async Task RespondOrFollowupAsync(
        string? text = null,
        bool ephemeral = false,
        Embed? embed = null,
        MessageComponent? components = null)
    {
        if (Context.Interaction.HasResponded)
            await FollowupAsync(text, ephemeral: ephemeral, embed: embed, components: components);
        else
            await RespondAsync(text, ephemeral: ephemeral, embed: embed, components: components);
    }

    private async ValueTask<CustomPlayer?> GetPlayerAsync(bool connectToVoiceChannel = true)
    {
        if (Context.User is not IGuildUser guildUser)
        {
            await RespondOrFollowupAsync("You must be in a guild to use this command", true);
            return null;
        }

        var voiceChannelId = guildUser.VoiceChannel?.Id;
        if (connectToVoiceChannel && voiceChannelId is null)
        {
            await RespondOrFollowupAsync("You must be connected to a voice channel to use this command.", true);
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

        logger.LogWarning("Failed to retrieve player for Guild {GuildId} by User {UserId}. Status: {Status}",
            Context.Guild.Id, Context.User.Id, result.Status);

        await RespondOrFollowupAsync(errorMessage, true);
        return null;
    }
}