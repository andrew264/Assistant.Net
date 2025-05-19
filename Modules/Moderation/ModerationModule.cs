using System.Net;
using Discord;
using Discord.Commands;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Moderation;

[RequireContext(ContextType.Guild)]
public class ModerationModule(ILogger<ModerationModule> logger) : ModuleBase<SocketCommandContext>
{
    private const int MaxMessagesPerBulkDelete = 100;
    private const int ManageGuildLimit = 20;
    private const int AdminLimit = 420;
    private static readonly TimeSpan FourteenDays = TimeSpan.FromDays(14);

    [Command("clear", RunMode = RunMode.Async)]
    [Alias("rm", "del")]
    [Summary("Deletes a specified number of recent messages.")]
    [RequireUserPermission(GuildPermission.ManageMessages)]
    public async Task ClearMessagesAsync(int count = 0)
    {
        if (count <= 0)
        {
            await ReplyAsync("Please specify a positive number of messages to delete.",
                allowedMentions: AllowedMentions.None).ConfigureAwait(false);
            return;
        }

        if (Context.Channel is not SocketTextChannel textChannel)
        {
            await ReplyAsync("This command can only be used in text channels.", allowedMentions: AllowedMentions.None).ConfigureAwait(false);
            return;
        }

        if (Context.User is not SocketGuildUser guildUser) return;

        var botUser = Context.Guild.CurrentUser;
        if (!botUser.GetPermissions(textChannel).ManageMessages)
        {
            await ReplyAsync("I don't have permission to delete messages in this channel.",
                allowedMentions: AllowedMentions.None).ConfigureAwait(false);
            return;
        }

        // Determine user's allowed limit
        var allowedLimit = ManageGuildLimit;
        if (guildUser.GuildPermissions.Administrator)
            allowedLimit = AdminLimit;
        else if (guildUser.GuildPermissions.ManageGuild) allowedLimit = ManageGuildLimit;

        if (count > allowedLimit)
        {
            await ReplyAsync(
                $"You can only delete up to {allowedLimit} messages with your current permissions.",
                allowedMentions: AllowedMentions.None).ConfigureAwait(false);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var fourteenDaysAgo = now - FourteenDays;
        var messagesToDelete = new List<IMessage>();
        var messagesFetched = 0;
        var totalToDelete = count + 1;

        try
        {
            while (messagesFetched < totalToDelete)
            {
                var fetchLimit = Math.Min(MaxMessagesPerBulkDelete, totalToDelete - messagesFetched);
                var batch = await Context.Channel.GetMessagesAsync(fetchLimit).FlattenAsync().ConfigureAwait(false);
                var messageBatch = batch as IMessage[] ?? batch.ToArray();
                var validBatch = messageBatch.Where(m => m.Timestamp > fourteenDaysAgo).ToList();

                messagesToDelete.AddRange(validBatch);
                messagesFetched += messageBatch.Length;

                if (messageBatch.Length < fetchLimit || validBatch.Count == 0) break;

                if (validBatch.Min(m => m.Timestamp) < fourteenDaysAgo) break;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch messages for clear command in {ChannelId}", Context.Channel.Id);
            await ReplyAsync("Failed to fetch messages to delete.", allowedMentions: AllowedMentions.None).ConfigureAwait(false);
            return;
        }

        messagesToDelete = messagesToDelete
            .Where(m => m.Timestamp > fourteenDaysAgo)
            .DistinctBy(m => m.Id)
            .OrderByDescending(m => m.Timestamp)
            .Take(totalToDelete)
            .ToList();

        var commandMessage = messagesToDelete.FirstOrDefault(m => m.Id == Context.Message.Id);
        if (commandMessage != null) messagesToDelete.Remove(commandMessage);

        if (messagesToDelete.Count == 0)
        {
            if (commandMessage != null)
                try
                {
                    await Context.Message.DeleteAsync().ConfigureAwait(false);
                }
                catch
                {
                    // ignored
                }

            await ReplyAsync("No recent messages found to delete (messages must be less than 14 days old).",
                allowedMentions: AllowedMentions.None).ConfigureAwait(false);
            return;
        }

        var deletedCount = 0;
        try
        {
            // Bulk delete
            foreach (var chunk in messagesToDelete.Chunk(MaxMessagesPerBulkDelete))
            {
                if (chunk.Length == 0) continue;
                await textChannel.DeleteMessagesAsync(chunk).ConfigureAwait(false);
                deletedCount += chunk.Length;
                if (messagesToDelete.Count > MaxMessagesPerBulkDelete)
                    await Task.Delay(1100).ConfigureAwait(false);
            }

            if (commandMessage != null)
                try
                {
                    await Context.Message.DeleteAsync().ConfigureAwait(false);
                }
                catch
                {
                    // ignored
                }

            var message = await ReplyAsync($"🗑️ Successfully deleted {deletedCount} message(s).",
                allowedMentions: AllowedMentions.None).ConfigureAwait(false);
            logger.LogInformation(
                "[MOD ACTION] clear: {User} deleted {Count} messages in #{Channel} ({Guild})",
                Context.User, deletedCount, textChannel.Name, Context.Guild.Name);
            _ = Task.Delay(5000).ContinueWith(_ => message.DeleteAsync().ConfigureAwait(false)).ConfigureAwait(false);
        }
        catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
        {
            logger.LogError(ex, "Permission error during clear in {ChannelId}", textChannel.Id);
            await ReplyAsync("I don't have permission to delete messages.", allowedMentions: AllowedMentions.None).ConfigureAwait(false);
        }
        catch (ArgumentOutOfRangeException)
        {
            await ReplyAsync(
                "Cannot delete messages - ensure they are less than 14 days old and there's at least one message to delete.",
                allowedMentions: AllowedMentions.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during clear execution in {ChannelId}", textChannel.Id);
            await ReplyAsync("An unexpected error occurred while deleting messages.",
                allowedMentions: AllowedMentions.None).ConfigureAwait(false);
        }
    }
}