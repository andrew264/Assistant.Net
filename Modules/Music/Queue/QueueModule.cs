using Assistant.Net.Modules.Music.Helpers;
using Assistant.Net.Services;
using Discord;
using Discord.Commands;
using Lavalink4NET.Clients;
using Lavalink4NET.Players;

namespace Assistant.Net.Modules.Music.Queue;

[Group("queue")]
[Alias("q")]
[Summary("Manage the song queue.")]
public class QueueModule(
    MusicService musicService)
    : ModuleBase<SocketCommandContext>
{
    [Command]
    [Alias("view", "list", "show", "np", "nowplaying")]
    [Summary("View the current song queue.")]
    public async Task ViewQueueAsync()
    {
        var (player, retrieveStatus) = await musicService.GetPlayerForContextAsync(
            Context.Guild, Context.User, Context.Channel,
            PlayerChannelBehavior.None,
            MemberVoiceStateBehavior.Ignore);

        if (player is null)
        {
            var errorMessage = MusicModuleHelpers.GetPlayerRetrieveErrorMessage(retrieveStatus);
            await ReplyAsync(errorMessage, allowedMentions: AllowedMentions.None);
            return;
        }

        if (player.CurrentTrack is null && player.Queue.IsEmpty)
        {
            await ReplyAsync("The queue is empty.", allowedMentions: AllowedMentions.None);
            return;
        }

        var reply = await ReplyAsync("Loading Queue...");

        var (embed, components, _) = musicService.BuildQueueEmbed(player, 0, reply.Id, Context.User.Id);
        await reply.ModifyAsync(props =>
        {
            props.Content = "";
            props.Embed = embed;
            props.Components = components;
        });
    }


    [Command("remove")]
    [Alias("rm", "delete", "del")]
    [Summary("Remove a song from the queue by its 1-based index.")]
    public async Task RemoveAsync([Summary("The index of the song to remove.")] int index)
    {
        var (player, retrieveStatus) = await musicService.GetPlayerForContextAsync(
            Context.Guild, Context.User, Context.Channel,
            PlayerChannelBehavior.None,
            MemberVoiceStateBehavior.RequireSame);

        if (player is null)
        {
            var errorMessage = MusicModuleHelpers.GetPlayerRetrieveErrorMessage(retrieveStatus);
            await ReplyAsync(errorMessage, allowedMentions: AllowedMentions.None);
            return;
        }

        var (_, _, message) = await musicService.RemoveFromQueueAsync(player, index);
        await ReplyAsync(message, allowedMentions: AllowedMentions.None);
    }

    [Command("clear")]
    [Alias("clr")]
    [Summary("Clear the entire song queue.")]
    public async Task ClearAsync()
    {
        var (player, retrieveStatus) = await musicService.GetPlayerForContextAsync(
            Context.Guild, Context.User, Context.Channel,
            PlayerChannelBehavior.None,
            MemberVoiceStateBehavior.RequireSame);

        if (player is null)
        {
            var errorMessage = MusicModuleHelpers.GetPlayerRetrieveErrorMessage(retrieveStatus);
            await ReplyAsync(errorMessage, allowedMentions: AllowedMentions.None);
            return;
        }

        var (success, message) = await musicService.ClearQueueAsync(player);
        await ReplyAsync(message, allowedMentions: AllowedMentions.None);
        if (success) await Context.Message.AddReactionAsync(new Emoji("✅"));
    }

    [Command("shuffle")]
    [Alias("shfl")]
    [Summary("Shuffle the songs in the queue.")]
    public async Task ShuffleAsync()
    {
        var (player, retrieveStatus) = await musicService.GetPlayerForContextAsync(
            Context.Guild, Context.User, Context.Channel,
            PlayerChannelBehavior.None,
            MemberVoiceStateBehavior.RequireSame);

        if (player is null)
        {
            var errorMessage = MusicModuleHelpers.GetPlayerRetrieveErrorMessage(retrieveStatus);
            await ReplyAsync(errorMessage, allowedMentions: AllowedMentions.None);
            return;
        }

        var (success, message) = await musicService.ShuffleQueueAsync(player);
        var reply = await ReplyAsync(message, allowedMentions: AllowedMentions.None);
        if (success) await Context.Message.AddReactionAsync(new Emoji("🔀"));

        var (embed, components, _) = musicService.BuildQueueEmbed(player, 0, reply.Id, Context.User.Id);
        await Task.Delay(2000);
        await reply.ModifyAsync(props =>
        {
            props.Content = "";
            props.Embed = embed;
            props.Components = components;
        });
    }

    [Command("move")]
    [Alias("mv")]
    [Summary("Move a song within the queue.")]
    public async Task MoveAsync(
        [Summary("The current 1-based index of the song.")]
        int fromIndex,
        [Summary("The new 1-based index for the song.")]
        int toIndex)
    {
        var (player, retrieveStatus) = await musicService.GetPlayerForContextAsync(
            Context.Guild, Context.User, Context.Channel,
            PlayerChannelBehavior.None,
            MemberVoiceStateBehavior.RequireSame);

        if (player is null)
        {
            var errorMessage = MusicModuleHelpers.GetPlayerRetrieveErrorMessage(retrieveStatus);
            await ReplyAsync(errorMessage, allowedMentions: AllowedMentions.None);
            return;
        }

        var (_, _, message) = await musicService.MoveInQueueAsync(player, fromIndex, toIndex);
        await ReplyAsync(message, allowedMentions: AllowedMentions.None);
    }

    [Command("loop")]
    [Summary("Toggle looping for the entire queue.")]
    public async Task LoopQueueAsync()
    {
        var (player, retrieveStatus) = await musicService.GetPlayerForContextAsync(
            Context.Guild, Context.User, Context.Channel,
            PlayerChannelBehavior.None,
            MemberVoiceStateBehavior.RequireSame);

        if (player is null)
        {
            var errorMessage = MusicModuleHelpers.GetPlayerRetrieveErrorMessage(retrieveStatus);
            await ReplyAsync(errorMessage, allowedMentions: AllowedMentions.None);
            return;
        }

        var (_, message) = musicService.ToggleQueueLoop(player);
        await ReplyAsync(message, allowedMentions: AllowedMentions.None);
    }
}