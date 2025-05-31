using Assistant.Net.Modules.Music.Base;
using Assistant.Net.Modules.Music.Helpers;
using Assistant.Net.Services.Music;
using Discord;
using Discord.Commands;
using Lavalink4NET.Players;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Music.Commands;

public class CommandsModule(MusicService musicService, ILogger<CommandsModule> logger)
    : MusicPrefixModuleBase(musicService, logger)
{
    [Command("skip")]
    [Alias("s")]
    [Summary("Skip songs that are in queue")]
    public async Task SkipPrefixAsync([Summary("The index of the song in the queue to skip.")] int index = 0)
    {
        var (player, isError) = await GetVerifiedPlayerAsync().ConfigureAwait(false);
        if (isError || player is null) return;

        var (_, _, message) = await MusicService.SkipTrackAsync(player, Context.User, index).ConfigureAwait(false);
        await ReplyAsync(message).ConfigureAwait(false);
    }

    [Command("loop")]
    [Alias("l")]
    [Summary("Toggles looping for the current song or the entire queue.")]
    public async Task LoopPrefixAsync()
    {
        var (player, isError) = await GetVerifiedPlayerAsync().ConfigureAwait(false);
        if (isError || player is null) return;

        var message = MusicService.ToggleLoopMode(player, Context.User);
        await ReplyAsync(message).ConfigureAwait(false);
    }

    [Command("volume")]
    [Alias("vol")]
    [Summary("Sets or displays the player volume (0-200%).")]
    public async Task VolumePrefixAsync([Summary("The volume to set (0-200).")] int? volume = null)
    {
        var (player, isError) = await GetVerifiedPlayerAsync(customErrorMessageProvider: status =>
            status == PlayerRetrieveStatus.BotNotConnected
                ? "I am not connected to a voice channel."
                : MusicModuleHelpers.GetPlayerRetrieveErrorMessage(status)
        ).ConfigureAwait(false);
        if (isError || player is null) return;

        if (volume is null)
        {
            await ReplyAsync($"Current Volume: {(int)MusicService.GetCurrentVolumePercent(player)}%")
                .ConfigureAwait(false);
            return;
        }

        var (_, message) = await MusicService.SetVolumeAsync(player, Context.User, volume.Value).ConfigureAwait(false);
        await ReplyAsync(message).ConfigureAwait(false);
    }

    [Command("stop")]
    [Alias("leave", "disconnect", "dc")]
    [Summary("Stops the music, clears the queue, and disconnects the bot.")]
    public async Task StopPrefixAsync()
    {
        var (player, isError) = await GetVerifiedPlayerAsync(customErrorMessageProvider: status =>
            status == PlayerRetrieveStatus.BotNotConnected
                ? "I am not playing anything right now."
                : MusicModuleHelpers.GetPlayerRetrieveErrorMessage(status)
        ).ConfigureAwait(false);
        if (isError || player is null) return;

        await MusicService.StopPlaybackAsync(player, Context.User).ConfigureAwait(false);
        await ReplyAsync("Thanks for Listening.").ConfigureAwait(false);
        await Context.Message.AddReactionAsync(new Emoji("👋")).ConfigureAwait(false);
    }

    [Command("skipto")]
    [Alias("st")]
    [Summary("Skips to a specific song in the queue.")]
    public async Task SkipToPrefixAsync([Summary("The index of the song to skip to.")] int index = 0)
    {
        var (player, isError) = await GetVerifiedPlayerAsync().ConfigureAwait(false);
        if (isError || player is null) return;

        var (_, message) = await MusicService.SkipToTrackAsync(player, Context.User, index).ConfigureAwait(false);
        await ReplyAsync(message).ConfigureAwait(false);
    }

    [Command("seek")]
    [Summary("Seeks to a specific time in the current song.")]
    public async Task SeekPrefixAsync([Summary("Time to seek to in MM:SS format")] string time = "0")
    {
        var (player, isError) = await GetVerifiedPlayerAsync().ConfigureAwait(false);
        if (isError || player is null) return;

        var (_, message) = await MusicService.SeekAsync(player, Context.User, time).ConfigureAwait(false);
        await ReplyAsync(message).ConfigureAwait(false);
    }
}