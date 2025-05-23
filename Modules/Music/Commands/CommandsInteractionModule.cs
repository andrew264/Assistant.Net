using Assistant.Net.Modules.Music.Helpers;
using Assistant.Net.Services;
using Discord;
using Discord.Interactions;
using Lavalink4NET.Clients;
using Lavalink4NET.Players;

namespace Assistant.Net.Modules.Music.Commands;

[CommandContextType(InteractionContextType.Guild)]
public class CommandsInteractionModule(MusicService musicService)
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
                components: components, allowedMentions: allowedMentions ?? AllowedMentions.None).ConfigureAwait(false);
        else
            await RespondAsync(text, ephemeral: ephemeral, embed: embed, components: components,
                allowedMentions: allowedMentions ?? AllowedMentions.None).ConfigureAwait(false);
    }

    [SlashCommand("skip", "Skip songs that are in queue")]
    public async Task SkipAsync([Summary(description: "The index of the song in the queue to skip.")] int index = 0)
    {
        var (player, retrieveStatus) = await musicService.GetPlayerForContextAsync(
            Context.Guild, Context.User, Context.Channel, PlayerChannelBehavior.None, MemberVoiceStateBehavior.Ignore).ConfigureAwait(false);

        if (player is null)
        {
            var errorMessage = MusicModuleHelpers.GetPlayerRetrieveErrorMessage(retrieveStatus);
            await RespondOrFollowupAsync(errorMessage, true).ConfigureAwait(false);
            return;
        }

        await DeferAsync().ConfigureAwait(false);
        var (success, _, message) = await musicService.SkipTrackAsync(player, Context.User, index).ConfigureAwait(false);
        await RespondOrFollowupAsync(message, !success).ConfigureAwait(false);
    }

    [SlashCommand("loop", "Loops the current song or the entire queue.")]
    public async Task LoopAsync()
    {
        var (player, retrieveStatus) = await musicService.GetPlayerForContextAsync(
            Context.Guild, Context.User, Context.Channel, PlayerChannelBehavior.None, MemberVoiceStateBehavior.Ignore).ConfigureAwait(false);
        if (player is null)
        {
            var errorMessage = MusicModuleHelpers.GetPlayerRetrieveErrorMessage(retrieveStatus);
            await RespondOrFollowupAsync(errorMessage, true).ConfigureAwait(false);
            return;
        }

        await DeferAsync().ConfigureAwait(false);
        var message = musicService.ToggleLoopMode(player, Context.User);
        await RespondOrFollowupAsync(message).ConfigureAwait(false);
    }

    [SlashCommand("volume", "Sets the player volume (0 - 200%)")]
    public async Task VolumeAsync([Summary(description: "Volume to set [0 - 200] %")] int? volume = null)
    {
        var (player, retrieveStatus) = await musicService.GetPlayerForContextAsync(
            Context.Guild, Context.User, Context.Channel, PlayerChannelBehavior.None, MemberVoiceStateBehavior.Ignore).ConfigureAwait(false);
        switch (player)
        {
            case null when retrieveStatus != PlayerRetrieveStatus.BotNotConnected:
            {
                var errorMessage = MusicModuleHelpers.GetPlayerRetrieveErrorMessage(retrieveStatus);
                await RespondOrFollowupAsync(errorMessage, true).ConfigureAwait(false);
                return;
            }
            case null:
                await RespondOrFollowupAsync("I am not connected to a voice channel.", true).ConfigureAwait(false);
                return;
        }

        await DeferAsync().ConfigureAwait(false);
        if (volume is null)
        {
            await RespondOrFollowupAsync($"Current Volume: {(int)musicService.GetCurrentVolumePercent(player)}%").ConfigureAwait(false);
            return;
        }

        var (success, message) = await musicService.SetVolumeAsync(player, Context.User, volume.Value).ConfigureAwait(false);
        await RespondOrFollowupAsync(message, !success).ConfigureAwait(false);
    }

    [SlashCommand("stop", "Stops the music and disconnects the bot from the voice channel")]
    public async Task StopAsync()
    {
        var (player, retrieveStatus) = await musicService.GetPlayerForContextAsync(
            Context.Guild, Context.User, Context.Channel, PlayerChannelBehavior.None, MemberVoiceStateBehavior.Ignore).ConfigureAwait(false);
        switch (player)
        {
            case null when retrieveStatus != PlayerRetrieveStatus.BotNotConnected:
            {
                var errorMessage = MusicModuleHelpers.GetPlayerRetrieveErrorMessage(retrieveStatus);
                await RespondOrFollowupAsync(errorMessage, true).ConfigureAwait(false);
                return;
            }
            case null:
                await RespondOrFollowupAsync("I am not playing anything right now.", true).ConfigureAwait(false);
                return;
        }

        await DeferAsync().ConfigureAwait(false);
        await musicService.StopPlaybackAsync(player, Context.User).ConfigureAwait(false);
        await RespondOrFollowupAsync("Thanks for Listening").ConfigureAwait(false);
    }

    [SlashCommand("skipto", "Skips to a specific song in the queue")]
    public async Task SkipToAsync([Summary(description: "Index of the song to skip to")] int index = 0)
    {
        var (player, retrieveStatus) = await musicService.GetPlayerForContextAsync(
            Context.Guild, Context.User, Context.Channel, PlayerChannelBehavior.None, MemberVoiceStateBehavior.Ignore).ConfigureAwait(false);
        if (player is null)
        {
            var errorMessage = MusicModuleHelpers.GetPlayerRetrieveErrorMessage(retrieveStatus);
            await RespondOrFollowupAsync(errorMessage, true).ConfigureAwait(false);
            return;
        }

        await DeferAsync().ConfigureAwait(false);
        var (success, message) = await musicService.SkipToTrackAsync(player, Context.User, index).ConfigureAwait(false);
        await RespondOrFollowupAsync(message, !success).ConfigureAwait(false);
    }

    [SlashCommand("seek", "Seeks to a specific time in the current song.")]
    public async Task Seek([Summary(description: "Time to seek to in MM:SS format")] string time = "0")
    {
        var (player, retrieveStatus) = await musicService.GetPlayerForContextAsync(
            Context.Guild, Context.User, Context.Channel, PlayerChannelBehavior.None, MemberVoiceStateBehavior.Ignore).ConfigureAwait(false);
        if (player is null)
        {
            var errorMessage = MusicModuleHelpers.GetPlayerRetrieveErrorMessage(retrieveStatus);
            await RespondOrFollowupAsync(errorMessage, true).ConfigureAwait(false);
            return;
        }

        await DeferAsync().ConfigureAwait(false);
        var (success, message) = await musicService.SeekAsync(player, Context.User, time).ConfigureAwait(false);
        await RespondOrFollowupAsync(message, !success).ConfigureAwait(false);
    }
}