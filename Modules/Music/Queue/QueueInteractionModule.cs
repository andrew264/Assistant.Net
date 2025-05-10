using Assistant.Net.Modules.Music.Helpers;
using Assistant.Net.Modules.Music.Player;
using Assistant.Net.Services;
using Discord;
using Discord.Interactions;
using Lavalink4NET.Clients;
using Lavalink4NET.Players;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Music.Queue;

[Group("queue", "Manage the song queue.")]
[CommandContextType(InteractionContextType.Guild)]
public class QueueInteractionModule(
    MusicService musicService,
    ILogger<QueueInteractionModule> logger)
    : InteractionModuleBase<SocketInteractionContext>
{
    private const int ItemsPerPage = 4;

    private async Task ModifyOrFollowupWithQueueAsync(CustomPlayer player, int currentPage, ulong interactionMessageId,
        bool ephemeral = false)
    {
        var (embed, components, error) =
            musicService.BuildQueueEmbed(player, currentPage, interactionMessageId, Context.User.Id);

        if (error != null)
        {
            try
            {
                await ModifyOriginalResponseAsync(props =>
                {
                    props.Content = error;
                    props.Embed = null;
                    props.Components = new ComponentBuilder().Build();
                });
            }
            catch
            {
                await FollowupAsync(error, ephemeral: true);
            }

            return;
        }

        try
        {
            await ModifyOriginalResponseAsync(props =>
            {
                props.Content = "";
                props.Embed = embed;
                props.Components = components;
            });
        }
        catch
        {
            if (!ephemeral)
                await FollowupAsync(embed: embed, components: components, ephemeral: ephemeral);
            else
                logger.LogWarning(
                    "Failed to modify original ephemeral response for queue. User: {UserId}, InteractionMsgId: {MsgId}",
                    Context.User.Id, interactionMessageId);
        }
    }


    [SlashCommand("view", "View the current song queue.")]
    public async Task ViewQueueAsync()
    {
        var (player, retrieveStatus) = await musicService.GetPlayerForContextAsync(
            Context.Guild, Context.User, Context.Channel,
            PlayerChannelBehavior.None,
            MemberVoiceStateBehavior.Ignore);

        if (player is null)
        {
            var errorMessage = MusicModuleHelpers.GetPlayerRetrieveErrorMessage(retrieveStatus);
            await RespondAsync(errorMessage, ephemeral: true);
            return;
        }

        await RespondAsync("Fetching queue...");
        var initialResponse = await GetOriginalResponseAsync();
        await ModifyOrFollowupWithQueueAsync(player, 1, initialResponse.Id);
    }

    [ComponentInteraction("assistant:queue_page_action:*:*:*:*", true)]
    public async Task HandleQueuePageAction(ulong requesterId, ulong interactionMessageId, int currentPage,
        string action)
    {
        if (Context.User.Id != requesterId)
        {
            await RespondAsync("This is not your queue interaction!", ephemeral: true);
            return;
        }

        await DeferAsync(true);

        var (player, retrieveStatus) = await musicService.GetPlayerForContextAsync(
            Context.Guild, Context.User, Context.Channel,
            PlayerChannelBehavior.None,
            MemberVoiceStateBehavior.Ignore);

        if (player is null)
        {
            var errorMessage = MusicModuleHelpers.GetPlayerRetrieveErrorMessage(retrieveStatus);
            try
            {
                await ModifyOriginalResponseAsync(props =>
                {
                    props.Content = errorMessage;
                    props.Embed = null;
                    props.Components = new ComponentBuilder().Build();
                });
            }
            catch
            {
                await FollowupAsync(errorMessage, ephemeral: true);
            }

            return;
        }

        var queueCount = player.Queue.Count;
        var totalPages = queueCount > 0 ? (int)Math.Ceiling((double)queueCount / ItemsPerPage) : 1;
        var newPage = currentPage;

        switch (action)
        {
            case "prev":
            {
                newPage = currentPage - 1;
                if (newPage < 1) newPage = totalPages;
                break;
            }
            case "next":
            {
                newPage = currentPage + 1;
                if (newPage > totalPages) newPage = 1;
                break;
            }
        }

        await ModifyOrFollowupWithQueueAsync(player, newPage, interactionMessageId);
    }

    [SlashCommand("remove", "Remove a song from the queue by its 1-based index.")]
    public async Task RemoveAsync([Summary("index", "The index of the song to remove from the queue.")] int index)
    {
        var (player, retrieveStatus) = await musicService.GetPlayerForContextAsync(
            Context.Guild, Context.User, Context.Channel,
            PlayerChannelBehavior.None,
            MemberVoiceStateBehavior.RequireSame);

        if (player is null)
        {
            var errorMessage = MusicModuleHelpers.GetPlayerRetrieveErrorMessage(retrieveStatus);
            await RespondAsync(errorMessage, ephemeral: true);
            return;
        }

        await DeferAsync();
        var (success, _, message) = await musicService.RemoveFromQueueAsync(player, index);
        await FollowupAsync(message, ephemeral: !success);
    }

    [SlashCommand("clear", "Clear the entire song queue.")]
    public async Task ClearAsync()
    {
        var (player, retrieveStatus) = await musicService.GetPlayerForContextAsync(
            Context.Guild, Context.User, Context.Channel,
            PlayerChannelBehavior.None,
            MemberVoiceStateBehavior.RequireSame);

        if (player is null)
        {
            var errorMessage = MusicModuleHelpers.GetPlayerRetrieveErrorMessage(retrieveStatus);
            await RespondAsync(errorMessage, ephemeral: true);
            return;
        }

        await DeferAsync();
        var (success, message) = await musicService.ClearQueueAsync(player);
        await FollowupAsync(message, ephemeral: !success);
    }

    [SlashCommand("shuffle", "Shuffle the songs in the queue.")]
    public async Task ShuffleAsync()
    {
        var (player, retrieveStatus) = await musicService.GetPlayerForContextAsync(
            Context.Guild, Context.User, Context.Channel,
            PlayerChannelBehavior.None,
            MemberVoiceStateBehavior.RequireSame);

        if (player is null)
        {
            var errorMessage = MusicModuleHelpers.GetPlayerRetrieveErrorMessage(retrieveStatus);
            await RespondAsync(errorMessage, ephemeral: true);
            return;
        }

        await DeferAsync();
        var (success, message) = await musicService.ShuffleQueueAsync(player);
        await FollowupAsync(message, ephemeral: !success);

        if (success)
        {
            var followupResponse = await GetOriginalResponseAsync();
            await ModifyOrFollowupWithQueueAsync(player, 1, followupResponse.Id);
        }
    }

    [SlashCommand("move", "Move a song within the queue.")]
    public async Task MoveAsync(
        [Summary("from_index", "The current 1-based index of the song.")]
        int fromIndex,
        [Summary("to_index", "The new 1-based index for the song.")]
        int toIndex)
    {
        var (player, retrieveStatus) = await musicService.GetPlayerForContextAsync(
            Context.Guild, Context.User, Context.Channel,
            PlayerChannelBehavior.None,
            MemberVoiceStateBehavior.RequireSame);

        if (player is null)
        {
            var errorMessage = MusicModuleHelpers.GetPlayerRetrieveErrorMessage(retrieveStatus);
            await RespondAsync(errorMessage, ephemeral: true);
            return;
        }

        await DeferAsync();
        var (success, _, message) = await musicService.MoveInQueueAsync(player, fromIndex, toIndex);
        await FollowupAsync(message, ephemeral: !success);
    }

    [SlashCommand("loop", "Toggle looping for the entire queue.")]
    public async Task LoopQueueAsync()
    {
        var (player, retrieveStatus) = await musicService.GetPlayerForContextAsync(
            Context.Guild, Context.User, Context.Channel,
            PlayerChannelBehavior.None,
            MemberVoiceStateBehavior.RequireSame);

        if (player is null)
        {
            var errorMessage = MusicModuleHelpers.GetPlayerRetrieveErrorMessage(retrieveStatus);
            await RespondAsync(errorMessage, ephemeral: true);
            return;
        }

        await DeferAsync();
        var (_, message) = musicService.ToggleQueueLoop(player);
        await FollowupAsync(message);
    }
}