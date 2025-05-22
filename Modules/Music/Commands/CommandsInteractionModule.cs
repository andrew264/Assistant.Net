using Assistant.Net.Modules.Music.Base;
using Assistant.Net.Modules.Music.Helpers;
using Assistant.Net.Services;
using Discord;
using Discord.Interactions;
using Lavalink4NET.Players;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Music.Commands;

[CommandContextType(InteractionContextType.Guild)]
public class CommandsInteractionModule(MusicService musicService, ILogger<CommandsInteractionModule> logger)
    : MusicInteractionModuleBase(musicService, logger)
{
    [SlashCommand("skip", "Skip songs that are in queue")]
    public async Task SkipAsync([Summary(description: "The index of the song in the queue to skip.")] int index = 0)
    {
        var (player, isError) = await GetVerifiedPlayerAsync().ConfigureAwait(false);
        if (isError || player is null) return;

        await DeferAsync().ConfigureAwait(false);
        var (success, _, message) =
            await MusicService.SkipTrackAsync(player, Context.User, index).ConfigureAwait(false);
        await RespondOrFollowupAsync(message, !success).ConfigureAwait(false);
    }

    [SlashCommand("loop", "Loops the current song or the entire queue.")]
    public async Task LoopAsync()
    {
        var (player, isError) = await GetVerifiedPlayerAsync().ConfigureAwait(false);
        if (isError || player is null) return;

        await DeferAsync().ConfigureAwait(false);
        var message = MusicService.ToggleLoopMode(player, Context.User);
        await RespondOrFollowupAsync(message).ConfigureAwait(false);
    }

    [SlashCommand("volume", "Sets the player volume (0 - 200%)")]
    public async Task VolumeAsync([Summary(description: "Volume to set [0 - 200] %")] int? volume = null)
    {
        var (player, isError) = await GetVerifiedPlayerAsync(customErrorMessageProvider: status =>
            status == PlayerRetrieveStatus.BotNotConnected
                ? "I am not connected to a voice channel."
                : MusicModuleHelpers.GetPlayerRetrieveErrorMessage(status)
        ).ConfigureAwait(false);
        if (isError || player is null) return;

        await DeferAsync().ConfigureAwait(false);
        if (volume is null)
        {
            await RespondOrFollowupAsync($"Current Volume: {(int)MusicService.GetCurrentVolumePercent(player)}%")
                .ConfigureAwait(false);
            return;
        }

        var (success, message) =
            await MusicService.SetVolumeAsync(player, Context.User, volume.Value).ConfigureAwait(false);
        await RespondOrFollowupAsync(message, !success).ConfigureAwait(false);
    }

    [SlashCommand("stop", "Stops the music and disconnects the bot from the voice channel")]
    public async Task StopAsync()
    {
        var (player, isError) = await GetVerifiedPlayerAsync(customErrorMessageProvider: status =>
            status == PlayerRetrieveStatus.BotNotConnected
                ? "I am not playing anything right now."
                : MusicModuleHelpers.GetPlayerRetrieveErrorMessage(status)
        ).ConfigureAwait(false);
        if (isError || player is null) return;

        await DeferAsync().ConfigureAwait(false);
        await MusicService.StopPlaybackAsync(player, Context.User).ConfigureAwait(false);
        await RespondOrFollowupAsync("Thanks for Listening").ConfigureAwait(false);
    }

    [SlashCommand("skipto", "Skips to a specific song in the queue")]
    public async Task SkipToAsync([Summary(description: "Index of the song to skip to")] int index = 0)
    {
        var (player, isError) = await GetVerifiedPlayerAsync().ConfigureAwait(false);
        if (isError || player is null) return;

        await DeferAsync().ConfigureAwait(false);
        var (success, message) = await MusicService.SkipToTrackAsync(player, Context.User, index).ConfigureAwait(false);
        await RespondOrFollowupAsync(message, !success).ConfigureAwait(false);
    }

    [SlashCommand("seek", "Seeks to a specific time in the current song.")]
    public async Task Seek([Summary(description: "Time to seek to in MM:SS format")] string time = "0")
    {
        var (player, isError) = await GetVerifiedPlayerAsync().ConfigureAwait(false);
        if (isError || player is null) return;

        await DeferAsync().ConfigureAwait(false);
        var (success, message) = await MusicService.SeekAsync(player, Context.User, time).ConfigureAwait(false);
        await RespondOrFollowupAsync(message, !success).ConfigureAwait(false);
    }
}