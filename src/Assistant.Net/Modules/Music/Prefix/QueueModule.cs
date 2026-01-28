using Assistant.Net.Services.Music;
using Discord;
using Discord.Commands;
using Lavalink4NET.Clients;
using Lavalink4NET.Players;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Music.Prefix;

[Group("queue")]
[Alias("q")]
[Summary("Manage the song queue.")]
public class QueueModule(MusicService musicService, ILogger<QueueModule> logger)
    : MusicPrefixModuleBase(musicService, logger)
{
    [Command]
    [Alias("view", "list", "show", "np", "nowplaying")]
    [Summary("View the current song queue.")]
    public async Task ViewQueueAsync()
    {
        var (player, isError) = await GetVerifiedPlayerAsync().ConfigureAwait(false);

        if (isError || player is null) return;

        if (player.CurrentTrack is null && player.Queue.IsEmpty)
        {
            await ReplyAsync("The queue is empty.", allowedMentions: AllowedMentions.None).ConfigureAwait(false);
            return;
        }

        var reply = await ReplyAsync("Loading Queue...").ConfigureAwait(false);

        var (components, _) = MusicService.BuildQueueComponents(player, 0, reply.Id, Context.User.Id);
        await reply.ModifyAsync(props =>
        {
            props.Content = "";
            props.Components = components;
            props.Flags = MessageFlags.ComponentsV2;
        }).ConfigureAwait(false);
    }


    [Command("remove")]
    [Alias("rm", "delete", "del")]
    [Summary("Remove a song from the queue by its 1-based index.")]
    public async Task RemoveAsync([Summary("The index of the song to remove.")] int index)
    {
        var (player, isError) =
            await GetVerifiedPlayerAsync(PlayerChannelBehavior.None, MemberVoiceStateBehavior.RequireSame)
                .ConfigureAwait(false);

        if (isError || player is null) return;

        var (_, _, message) = await MusicService.RemoveFromQueueAsync(player, index).ConfigureAwait(false);
        await ReplyAsync(message, allowedMentions: AllowedMentions.None).ConfigureAwait(false);
    }

    [Command("clear")]
    [Alias("clr")]
    [Summary("Clear the entire song queue.")]
    public async Task ClearAsync()
    {
        var (player, isError) =
            await GetVerifiedPlayerAsync(PlayerChannelBehavior.None, MemberVoiceStateBehavior.RequireSame)
                .ConfigureAwait(false);

        if (isError || player is null) return;

        var (success, message) = await MusicService.ClearQueueAsync(player).ConfigureAwait(false);
        await ReplyAsync(message, allowedMentions: AllowedMentions.None).ConfigureAwait(false);
        if (success) await Context.Message.AddReactionAsync(new Emoji("✅")).ConfigureAwait(false);
    }

    [Command("shuffle")]
    [Alias("shfl")]
    [Summary("Shuffle the songs in the queue.")]
    public async Task ShuffleAsync()
    {
        var (player, isError) =
            await GetVerifiedPlayerAsync(PlayerChannelBehavior.None, MemberVoiceStateBehavior.RequireSame)
                .ConfigureAwait(false);

        if (isError || player is null) return;

        var (success, message) = await MusicService.ShuffleQueueAsync(player).ConfigureAwait(false);
        var reply = await ReplyAsync(message, allowedMentions: AllowedMentions.None, flags: MessageFlags.ComponentsV2)
            .ConfigureAwait(false);
        if (success) await Context.Message.AddReactionAsync(new Emoji("🔀")).ConfigureAwait(false);

        var (components, _) = MusicService.BuildQueueComponents(player, 0, reply.Id, Context.User.Id);
        await Task.Delay(2000).ConfigureAwait(false);
        await reply.ModifyAsync(props =>
        {
            props.Content = "";
            props.Components = components;
            props.Flags = MessageFlags.ComponentsV2;
        }).ConfigureAwait(false);
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
        var (player, isError) =
            await GetVerifiedPlayerAsync(PlayerChannelBehavior.None, MemberVoiceStateBehavior.RequireSame)
                .ConfigureAwait(false);

        if (isError || player is null) return;

        var (_, _, message) = await MusicService.MoveInQueueAsync(player, fromIndex, toIndex).ConfigureAwait(false);
        await ReplyAsync(message, allowedMentions: AllowedMentions.None).ConfigureAwait(false);
    }

    [Command("loop")]
    [Summary("Toggle looping for the entire queue.")]
    public async Task LoopQueueAsync()
    {
        var (player, isError) =
            await GetVerifiedPlayerAsync(PlayerChannelBehavior.None, MemberVoiceStateBehavior.RequireSame)
                .ConfigureAwait(false);

        if (isError || player is null) return;

        var (_, message) = MusicService.ToggleQueueLoop(player);
        await ReplyAsync(message, allowedMentions: AllowedMentions.None).ConfigureAwait(false);
    }
}