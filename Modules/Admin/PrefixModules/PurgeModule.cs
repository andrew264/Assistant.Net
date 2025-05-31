using System.Net;
using Assistant.Net.Configuration;
using Discord;
using Discord.Commands;
using Discord.Net;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Admin.PrefixModules;

public class PurgeModule(ILogger<PurgeModule> logger, Config config)
    : ModuleBase<SocketCommandContext>
{
    [Command("purgeuser", RunMode = RunMode.Async)]
    [Alias("yeet")]
    [Summary("Deletes messages based on user ID and/or content keywords. Owner only.")]
    [RequireOwner]
    [RequireContext(ContextType.Guild)]
    public async Task PurgeUserAsync([Remainder] string? rawArgs = null)
    {
        if (Context.Channel is not ITextChannel textChannel)
        {
            await ReplyAsync("This command can only be used in text channels.").ConfigureAwait(false);
            return;
        }

        try
        {
            await Context.Message.DeleteAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete command invocation message for purgeuser.");
        }

        if (string.IsNullOrWhiteSpace(rawArgs))
        {
            await ReplyAsync(
                    $"Usage: `{config.Client.Prefix}purgeuser [userId] [comma,separated,keywords]` or `{config.Client.Prefix}purgeuser [comma,separated,keywords]`")
                .ConfigureAwait(false);
            return;
        }

        var args = rawArgs.Split([' '], StringSplitOptions.RemoveEmptyEntries).ToList();
        ulong? userId = null;

        if (args.Count > 0 && ulong.TryParse(args[0], out var parsedUserId))
        {
            userId = parsedUserId;
            args.RemoveAt(0);
        }

        var contentString = string.Join(" ", args);
        List<string> keywords = [];
        if (!string.IsNullOrWhiteSpace(contentString))
            keywords = contentString.Split(',')
                .Select(s => s.Trim().ToLowerInvariant())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

        if (!userId.HasValue && keywords.Count == 0)
        {
            await ReplyAsync("Please provide a UserID or comma-separated keywords to search for.")
                .ConfigureAwait(false);
            return;
        }

        logger.LogInformation(
            "[PURGE] Initiated by {User}. Target UserID: {TargetUserId}, Keywords: {KeywordsCount}",
            Context.User, userId?.ToString() ?? "None", keywords.Count);

        var allChannelMessages = new List<IMessage>();
        ulong? lastMessageId = null;
        const int fetchBatchSize = 100;
        const int maxMessagesToFetch = 20000;

        logger.LogInformation("[PURGE] Fetching messages from channel {ChannelName}...", textChannel.Name);
        while (allChannelMessages.Count < maxMessagesToFetch)
        {
            IEnumerable<IMessage> batch;
            if (lastMessageId == null)
                batch = await textChannel.GetMessagesAsync().FlattenAsync().ConfigureAwait(false);
            else
                batch = await textChannel.GetMessagesAsync(lastMessageId.Value, Direction.Before)
                    .FlattenAsync().ConfigureAwait(false);

            var batchList = batch.ToList();
            if (batchList.Count == 0) break;

            allChannelMessages.AddRange(batchList);
            lastMessageId = batchList.Last().Id;

            if (allChannelMessages.Count % 1000 == 0)
                logger.LogInformation("[PURGE] Fetched {Count} messages so far...", allChannelMessages.Count);

            if (batchList.Count < fetchBatchSize) break; // Last batch was smaller than requested
        }

        logger.LogInformation("[PURGE] Fetched a total of {Count} messages. Filtering now...",
            allChannelMessages.Count);

        var fourteenDaysAgo = DateTimeOffset.UtcNow.AddDays(-14);

        var matchingMessages = allChannelMessages
            .Where(msg => MassPurgeChecker(msg, userId, keywords) && msg.Id != Context.Message.Id)
            .ToList();

        if (matchingMessages.Count == 0)
        {
            var reply = await ReplyAsync("No matching messages found to delete.");
            _ = Task.Delay(30000).ContinueWith(_ => reply.DeleteAsync().ConfigureAwait(false));
            return;
        }

        var newerMessagesToDelete = matchingMessages.Where(m => m.Timestamp >= fourteenDaysAgo).ToList();
        var olderMessagesToDelete = matchingMessages.Where(m => m.Timestamp < fourteenDaysAgo).ToList();

        var deletedCount = 0;
        try
        {
            if (newerMessagesToDelete.Count > 0)
            {
                logger.LogInformation("[PURGE] Deleting {Count} newer messages (<= 14 days old) in batches.",
                    newerMessagesToDelete.Count);
                foreach (var chunk in newerMessagesToDelete.Chunk(100))
                {
                    switch (chunk.Length)
                    {
                        case 1:
                            await textChannel.DeleteMessageAsync(chunk[0]).ConfigureAwait(false);
                            deletedCount++;
                            break;
                        case > 1:
                            await textChannel.DeleteMessagesAsync(chunk.Where(m => m.Id != Context.Message.Id))
                                .ConfigureAwait(false);
                            deletedCount += chunk.Length;
                            break;
                    }

                    if (newerMessagesToDelete.Count > 100 && chunk.Length > 1)
                        await Task.Delay(1100).ConfigureAwait(false);
                }
            }

            // Delete older messages (one by one)
            if (olderMessagesToDelete.Count > 0)
            {
                logger.LogInformation("[PURGE] Deleting {Count} older messages (> 14 days old) one by one.",
                    olderMessagesToDelete.Count);
                foreach (var message in olderMessagesToDelete)
                    try
                    {
                        await textChannel.DeleteMessageAsync(message.Id).ConfigureAwait(false);
                        deletedCount++;
                        await Task.Delay(1100).ConfigureAwait(false); // Be respectful of rate limits
                    }
                    catch (HttpException exHttp) when (exHttp.HttpCode == HttpStatusCode.NotFound)
                    {
                        logger.LogWarning(
                            $"[PURGE] Older message {message.Id} already deleted or too old for single delete API access.");
                    }
                    catch (Exception exSingle)
                    {
                        logger.LogError(exSingle, "[PURGE] Failed to delete older message {MessageId} individually.",
                            message.Id);
                    }
            }

            logger.LogInformation("[PURGE] {User} deleted {Count} messages in {Guild}: #{Channel}",
                Context.User.Username, deletedCount, Context.Guild.Name, textChannel.Name);
            var reply = await ReplyAsync($"Deleted {deletedCount} messages.").ConfigureAwait(false);
            _ = Task.Delay(30000).ContinueWith(_ => reply.DeleteAsync());
        }
        catch (HttpException ex)
        {
            logger.LogError(ex, "[PURGE] HTTP error during message deletion.");
            var reply = await ReplyAsync($"An error occurred: {ex.Message}").ConfigureAwait(false);
            _ = Task.Delay(30000).ContinueWith(_ => reply.DeleteAsync());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[PURGE] General error during message deletion.");
            var reply = await ReplyAsync("An unexpected error occurred while deleting messages.").ConfigureAwait(false);
            _ = Task.Delay(30000).ContinueWith(_ => reply.DeleteAsync());
        }
    }

    private static bool MassPurgeChecker(IMessage msg, ulong? userIdFilter, List<string> keywords)
    {
        if (userIdFilter.HasValue && msg.Author.Id == userIdFilter.Value) return true;

        if (keywords.Count <= 0) return false;
        var authorNameLower = msg.Author.Username.ToLowerInvariant();
        if (keywords.Any(k => authorNameLower.Contains(k))) return true;

        // Check message content
        var contentLower = msg.Content.ToLowerInvariant();
        if (keywords.Any(k => contentLower.Contains(k))) return true;

        // Check embeds
        foreach (var embed in msg.Embeds)
        {
            if (embed.Title != null &&
                keywords.Any(k => embed.Title.Contains(k, StringComparison.InvariantCultureIgnoreCase))) return true;
            if (embed.Description != null && keywords.Any(k =>
                    embed.Description.Contains(k, StringComparison.InvariantCultureIgnoreCase))) return true;
            if (embed.Author?.Name != null && keywords.Any(k =>
                    embed.Author.Value.Name.Contains(k, StringComparison.InvariantCultureIgnoreCase))) return true;
            if (embed.Footer?.Text != null && keywords.Any(k =>
                    embed.Footer.Value.Text.Contains(k, StringComparison.InvariantCultureIgnoreCase))) return true;
            if (embed.Image?.Url != null && keywords.Any(k =>
                    embed.Image.Value.Url.Contains(k, StringComparison.InvariantCultureIgnoreCase))) return true;

            foreach (var field in embed.Fields)
            {
                if (!string.IsNullOrEmpty(field.Name) && keywords.Any(k =>
                        field.Name.Contains(k, StringComparison.InvariantCultureIgnoreCase))) return true;
                if (!string.IsNullOrEmpty(field.Value) && keywords.Any(k =>
                        field.Value.Contains(k, StringComparison.InvariantCultureIgnoreCase))) return true;
            }
        }

        // Check attachments
        return msg.Attachments.Any(attachment =>
            !string.IsNullOrEmpty(attachment.Filename) && keywords.Any(k =>
                attachment.Filename.Contains(k, StringComparison.InvariantCultureIgnoreCase)));
    }
}