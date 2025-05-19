using System.Net;
using Discord;
using Discord.Interactions;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Moderation;

[RequireContext(ContextType.Guild)]
public class ModerationInteractionModule(ILogger<ModerationInteractionModule> logger)
    : InteractionModuleBase<SocketInteractionContext>
{
    private const int MaxMessagesPerBulkDelete = 100;
    private static readonly TimeSpan FourteenDays = TimeSpan.FromDays(14);

    // --- Slash Command: /clear ---
    [SlashCommand("clear", "Deletes a specified number of recent messages.")]
    [RequireUserPermission(GuildPermission.ManageGuild)]
    [DefaultMemberPermissions(GuildPermission.ManageMessages)]
    public async Task ClearMessagesSlashAsync(
        [Summary("count", "Number of messages to delete (1-20).")] [MinValue(1)] [MaxValue(20)]
        int count)
    {
        await DeferAsync(true).ConfigureAwait(false);

        if (Context.Channel is not SocketTextChannel textChannel)
        {
            await FollowupAsync("This command can only be used in text channels.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var botUser = Context.Guild.CurrentUser;
        if (!botUser.GetPermissions(textChannel).ManageMessages)
        {
            await FollowupAsync("I don't have permission to delete messages in this channel.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var fourteenDaysAgo = now - FourteenDays;
        IEnumerable<IMessage> messagesToDelete;

        try
        {
            messagesToDelete = await Context.Channel.GetMessagesAsync(count).FlattenAsync().ConfigureAwait(false);
            messagesToDelete = messagesToDelete.Where(m => m.Timestamp > fourteenDaysAgo).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch messages for /clear command in {ChannelId}", Context.Channel.Id);
            await FollowupAsync("Failed to fetch messages to delete.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var messagesList = messagesToDelete.ToList();
        if (messagesList.Count == 0)
        {
            await FollowupAsync("No recent messages found to delete.",
                ephemeral: true).ConfigureAwait(false);
            return;
        }

        try
        {
            await textChannel.DeleteMessagesAsync(messagesList).ConfigureAwait(false);
            await FollowupAsync($"🗑️ Successfully deleted {messagesList.Count} message(s).", ephemeral: true).ConfigureAwait(false);
            logger.LogInformation(
                "[MOD ACTION] /clear: {User} deleted {Count} messages in #{Channel} ({Guild})",
                Context.User, messagesList.Count, textChannel.Name, Context.Guild.Name);
        }
        catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
        {
            logger.LogError(ex, "Permission error during /clear in {ChannelId}", textChannel.Id);
            await FollowupAsync("I don't have permission to delete messages.", ephemeral: true).ConfigureAwait(false);
        }
        catch (ArgumentOutOfRangeException)
        {
            await FollowupAsync(
                "Cannot delete messages - ensure they are less than 14 days old and there's at least one message to delete.",
                ephemeral: true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during /clear execution in {ChannelId}", textChannel.Id);
            await FollowupAsync("An unexpected error occurred while deleting messages.", ephemeral: true).ConfigureAwait(false);
        }
    }


    // --- Message Command: Delete till here ---
    [MessageCommand("Delete till here")]
    [RequireUserPermission(GuildPermission.ManageGuild)]
    [DefaultMemberPermissions(GuildPermission.ManageMessages)]
    public async Task DeleteTillHereAsync(IMessage targetMessage)
    {
        await DeferAsync(true).ConfigureAwait(false);

        if (Context.Channel is not SocketTextChannel textChannel)
        {
            await FollowupAsync("This command can only be used in text channels.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var botUser = Context.Guild.CurrentUser;
        if (!botUser.GetPermissions(textChannel).ManageMessages)
        {
            await FollowupAsync("I don't have permission to delete messages in this channel.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var fourteenDaysAgo = now - FourteenDays;

        if (targetMessage.Timestamp < fourteenDaysAgo)
        {
            await FollowupAsync("The selected message is too old (>14 days) to start deleting from.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        List<IMessage> messagesToDelete;
        try
        {
            var messagesAfter = await Context.Channel
                .GetMessagesAsync(targetMessage.Id, Direction.After)
                .FlattenAsync().ConfigureAwait(false);

            messagesToDelete = messagesAfter
                .Where(m => m.Timestamp > fourteenDaysAgo)
                .ToList();

            // messagesToDelete.Add(targetMessage);
            messagesToDelete = messagesToDelete.OrderBy(m => m.Timestamp).Take(MaxMessagesPerBulkDelete).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch messages for 'Delete till here' command in {ChannelId}",
                Context.Channel.Id);
            await FollowupAsync("Failed to fetch messages to delete.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (messagesToDelete.Count == 0) // Only the target message itself
        {
            await FollowupAsync("🗑️ No newer messages found!",
                ephemeral: true).ConfigureAwait(false);
            return;
        }

        try
        {
            await textChannel.DeleteMessagesAsync(messagesToDelete).ConfigureAwait(false);
            await FollowupAsync(
                $"🗑️ Successfully deleted {messagesToDelete.Count} message(s).",
                ephemeral: true).ConfigureAwait(false);
            logger.LogInformation(
                "[MOD ACTION] Delete till here: {User} deleted {Count} messages in #{Channel} ({Guild})",
                Context.User, messagesToDelete.Count, textChannel.Name, Context.Guild.Name);
        }
        catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
        {
            logger.LogError(ex, "Permission error during 'Delete till here' in {ChannelId}", textChannel.Id);
            await FollowupAsync("I don't have permission to delete messages.", ephemeral: true).ConfigureAwait(false);
        }
        catch (ArgumentOutOfRangeException)
        {
            await FollowupAsync(
                "Cannot delete messages - ensure they are less than 14 days old and there's at least one message to delete.",
                ephemeral: true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during 'Delete till here' execution in {ChannelId}", textChannel.Id);
            await FollowupAsync("An unexpected error occurred while deleting messages.", ephemeral: true).ConfigureAwait(false);
        }
    }
}