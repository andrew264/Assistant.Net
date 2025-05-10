using Assistant.Net.Modules.Music.Helpers;
using Assistant.Net.Services;
using Discord;
using Discord.Commands;
using Lavalink4NET.Clients;
using Lavalink4NET.Players;

namespace Assistant.Net.Modules.Music.Commands;

public class CommandsModule(MusicService musicService) : ModuleBase<SocketCommandContext>
{
    [Command("skip")]
    [Alias("s")]
    [Summary("Skip songs that are in queue")]
    public async Task SkipPrefixAsync([Summary("The index of the song in the queue to skip.")] int index = 0)
    {
        var (player, retrieveStatus) = await musicService.GetPlayerForContextAsync(
            Context.Guild, Context.User, Context.Channel, PlayerChannelBehavior.None, MemberVoiceStateBehavior.Ignore);
        if (player is null)
        {
            var errorMessage = MusicModuleHelpers.GetPlayerRetrieveErrorMessage(retrieveStatus);
            await ReplyAsync(errorMessage);
            return;
        }

        var (_, _, message) = await musicService.SkipTrackAsync(player, Context.User, index);
        await ReplyAsync(message);
    }

    [Command("loop")]
    [Alias("l")]
    [Summary("Toggles looping for the current song or the entire queue.")]
    public async Task LoopPrefixAsync()
    {
        var (player, retrieveStatus) = await musicService.GetPlayerForContextAsync(
            Context.Guild, Context.User, Context.Channel, PlayerChannelBehavior.None, MemberVoiceStateBehavior.Ignore);
        if (player is null)
        {
            var errorMessage = MusicModuleHelpers.GetPlayerRetrieveErrorMessage(retrieveStatus);
            await ReplyAsync(errorMessage);
            return;
        }

        var message = musicService.ToggleLoopMode(player, Context.User);
        await ReplyAsync(message);
    }

    [Command("volume")]
    [Alias("vol")]
    [Summary("Sets or displays the player volume (0-200%).")]
    public async Task VolumePrefixAsync([Summary("The volume to set (0-200).")] int? volume = null)
    {
        var (player, retrieveStatus) = await musicService.GetPlayerForContextAsync(
            Context.Guild, Context.User, Context.Channel, PlayerChannelBehavior.None, MemberVoiceStateBehavior.Ignore);
        switch (player)
        {
            case null when retrieveStatus != PlayerRetrieveStatus.BotNotConnected:
            {
                var errorMessage = MusicModuleHelpers.GetPlayerRetrieveErrorMessage(retrieveStatus);
                await ReplyAsync(errorMessage);
                return;
            }
            case null:
                await ReplyAsync("I am not connected to a voice channel.");
                return;
        }

        if (volume is null)
        {
            await ReplyAsync($"Current Volume: {(int)musicService.GetCurrentVolumePercent(player)}%");
            return;
        }

        var (_, message) = await musicService.SetVolumeAsync(player, Context.User, volume.Value);
        await ReplyAsync(message);
    }

    [Command("stop")]
    [Alias("leave", "disconnect", "dc")]
    [Summary("Stops the music, clears the queue, and disconnects the bot.")]
    public async Task StopPrefixAsync()
    {
        var (player, retrieveStatus) = await musicService.GetPlayerForContextAsync(
            Context.Guild, Context.User, Context.Channel, PlayerChannelBehavior.None, MemberVoiceStateBehavior.Ignore);
        switch (player)
        {
            case null when retrieveStatus != PlayerRetrieveStatus.BotNotConnected:
            {
                var errorMessage = MusicModuleHelpers.GetPlayerRetrieveErrorMessage(retrieveStatus);
                await ReplyAsync(errorMessage);
                return;
            }
            case null:
                await ReplyAsync("I am not playing anything right now.");
                return;
        }

        await musicService.StopPlaybackAsync(player, Context.User);
        await ReplyAsync("Thanks for Listening.");
        await Context.Message.AddReactionAsync(new Emoji("👋"));
    }

    [Command("skipto")]
    [Alias("st")]
    [Summary("Skips to a specific song in the queue.")]
    public async Task SkipToPrefixAsync([Summary("The index of the song to skip to.")] int index = 0)
    {
        var (player, retrieveStatus) = await musicService.GetPlayerForContextAsync(
            Context.Guild, Context.User, Context.Channel, PlayerChannelBehavior.None, MemberVoiceStateBehavior.Ignore);
        if (player is null)
        {
            var errorMessage = MusicModuleHelpers.GetPlayerRetrieveErrorMessage(retrieveStatus);
            await ReplyAsync(errorMessage);
            return;
        }

        var (_, message) = await musicService.SkipToTrackAsync(player, Context.User, index);
        await ReplyAsync(message);
    }

    [Command("seek")]
    [Summary("Seeks to a specific time in the current song.")]
    public async Task SeekPrefixAsync([Summary("Time to seek to in MM:SS format")] string time = "0")
    {
        var (player, retrieveStatus) = await musicService.GetPlayerForContextAsync(
            Context.Guild, Context.User, Context.Channel, PlayerChannelBehavior.None, MemberVoiceStateBehavior.Ignore);
        if (player is null)
        {
            var errorMessage = MusicModuleHelpers.GetPlayerRetrieveErrorMessage(retrieveStatus);
            await ReplyAsync(errorMessage);
            return;
        }

        var (_, message) = await musicService.SeekAsync(player, Context.User, time);
        await ReplyAsync(message);
    }
}