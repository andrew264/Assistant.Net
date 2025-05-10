using Assistant.Net.Modules.Music.PlayCommand;
using Assistant.Net.Modules.Music.Player;
using Assistant.Net.Services;
using Discord;
using Discord.Interactions;
using Lavalink4NET.Clients;
using Lavalink4NET.Players;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Music.Commands;

[CommandContextType(InteractionContextType.Guild)]
public class CommandsInteractionModule(
    ILogger<CommandsInteractionModule> logger,
    MusicService musicService)
    : InteractionModuleBase<SocketInteractionContext>
{
    private async Task RespondOrFollowupAsync(
        string? text = null,
        bool ephemeral = false,
        Embed? embed = null,
        MessageComponent? components = null,
        AllowedMentions? allowedMentions = null)
    {
        if (Context.Interaction.HasResponded)
            await FollowupAsync(text, ephemeral: ephemeral, embeds: embed != null ? [embed] : null,
                components: components, allowedMentions: allowedMentions ?? AllowedMentions.None);
        else
            await RespondAsync(text, ephemeral: ephemeral, embed: embed, components: components,
                allowedMentions: allowedMentions ?? AllowedMentions.None);
    }

    private async ValueTask<(CustomPlayer? Player, PlayerRetrieveStatus Status)> GetPlayerFromServiceAsync(
        bool connectToVoiceChannel = true)
    {
        if (Context.User is not IGuildUser guildUser)
        {
            logger.LogError("GetPlayerFromServiceAsync called by non-guild user {UserId}", Context.User.Id);
            return (null, PlayerRetrieveStatus.UserNotInVoiceChannel);
        }

        if (Context.Channel is ITextChannel textChannel)
            return await musicService.GetPlayerAsync(
                Context.Guild.Id,
                guildUser.VoiceChannel?.Id,
                textChannel,
                connectToVoiceChannel ? PlayerChannelBehavior.Join : PlayerChannelBehavior.None,
                connectToVoiceChannel ? MemberVoiceStateBehavior.RequireSame : MemberVoiceStateBehavior.Ignore);

        logger.LogError("GetPlayerFromServiceAsync called from non-text channel {ChannelId}", Context.Channel.Id);
        return (null, PlayerRetrieveStatus.UserNotInVoiceChannel);
    }

    [SlashCommand("skip", "Skip songs that are in queue")]
    public async Task SkipAsync([Summary(description: "The index of the song in the queue to skip.")] int index = 0)
    {
        var (player, retrieveStatus) = await GetPlayerFromServiceAsync(false);
        if (player is null)
        {
            var errorMessage = PlayInteractionModuleHelper.GetPlayerRetrieveErrorMessage(retrieveStatus);
            await RespondOrFollowupAsync(errorMessage, true);
            return;
        }

        await DeferAsync();
        var (success, _, message) = await musicService.SkipTrackAsync(player, Context.User, index);
        await RespondOrFollowupAsync(message, !success);
    }

    [SlashCommand("loop", "Loops the current song or the entire queue.")]
    public async Task LoopAsync()
    {
        var (player, retrieveStatus) = await GetPlayerFromServiceAsync(false);
        if (player is null)
        {
            var errorMessage = PlayInteractionModuleHelper.GetPlayerRetrieveErrorMessage(retrieveStatus);
            await RespondOrFollowupAsync(errorMessage, true);
            return;
        }

        await DeferAsync();
        var message = musicService.ToggleLoopMode(player, Context.User);
        await RespondOrFollowupAsync(message);
    }

    [SlashCommand("volume", "Sets the player volume (0 - 200%)")]
    public async Task VolumeAsync([Summary(description: "Volume to set [0 - 200] %")] int? volume = null)
    {
        var (player, retrieveStatus) = await GetPlayerFromServiceAsync(false);
        switch (player)
        {
            case null when retrieveStatus != PlayerRetrieveStatus.BotNotConnected:
            {
                var errorMessage = PlayInteractionModuleHelper.GetPlayerRetrieveErrorMessage(retrieveStatus);
                await RespondOrFollowupAsync(errorMessage, true);
                return;
            }
            case null:
                await RespondOrFollowupAsync("I am not connected to a voice channel.", true);
                return;
        }

        await DeferAsync();
        if (volume is null)
        {
            await RespondOrFollowupAsync($"Current Volume: {(int)musicService.GetCurrentVolumePercent(player)}%");
            return;
        }

        var (success, message) = await musicService.SetVolumeAsync(player, Context.User, volume.Value);
        await RespondOrFollowupAsync(message, !success);
    }

    [SlashCommand("stop", "Stops the music and disconnects the bot from the voice channel")]
    public async Task StopAsync()
    {
        var (player, retrieveStatus) = await GetPlayerFromServiceAsync(false);
        switch (player)
        {
            case null when retrieveStatus != PlayerRetrieveStatus.BotNotConnected:
            {
                var errorMessage = PlayInteractionModuleHelper.GetPlayerRetrieveErrorMessage(retrieveStatus);
                await RespondOrFollowupAsync(errorMessage, true);
                return;
            }
            case null:
                await RespondOrFollowupAsync("I am not playing anything right now.", true);
                return;
        }

        await DeferAsync();
        await musicService.StopPlaybackAsync(player, Context.User);
        await RespondOrFollowupAsync("Thanks for Listening");
    }

    [SlashCommand("skipto", "Skips to a specific song in the queue")]
    public async Task SkipToAsync([Summary(description: "Index of the song to skip to")] int index = 0)
    {
        var (player, retrieveStatus) = await GetPlayerFromServiceAsync(false);
        if (player is null)
        {
            var errorMessage = PlayInteractionModuleHelper.GetPlayerRetrieveErrorMessage(retrieveStatus);
            await RespondOrFollowupAsync(errorMessage, true);
            return;
        }

        await DeferAsync();
        var (success, message) = await musicService.SkipToTrackAsync(player, Context.User, index);
        await RespondOrFollowupAsync(message, !success);
    }

    [SlashCommand("seek", "Seeks to a specific time in the current song.")]
    public async Task Seek([Summary(description: "Time to seek to in MM:SS format")] string time = "0")
    {
        var (player, retrieveStatus) = await GetPlayerFromServiceAsync(false);
        if (player is null)
        {
            var errorMessage = PlayInteractionModuleHelper.GetPlayerRetrieveErrorMessage(retrieveStatus);
            await RespondOrFollowupAsync(errorMessage, true);
            return;
        }

        await DeferAsync();
        var (success, message) = await musicService.SeekAsync(player, Context.User, time);
        await RespondOrFollowupAsync(message, !success);
    }
}