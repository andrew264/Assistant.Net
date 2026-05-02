using System.Text;
using Assistant.Net.Data.Entities;
using Assistant.Net.Data.Repositories.Interfaces;
using Assistant.Net.Options;
using Assistant.Net.Services.Core;
using Assistant.Net.Utilities;
using Discord;
using Discord.Net;
using Discord.Webhook;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Net.Services.Features;

public class DmRelayService(
    DiscordSocketClient client,
    IOptions<DiscordOptions> options,
    ILogger<DmRelayService> logger,
    IHttpClientFactory httpClientFactory,
    WebhookService webhookService,
    IUnitOfWorkFactory uowFactory)
    : IHostedService
{
    private readonly DiscordOptions _options = options.Value;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        client.MessageReceived += HandleMessageReceivedAsync;
        client.MessageUpdated += HandleMessageUpdatedAsync;
        client.MessageDeleted += HandleMessageDeletedAsync;

        logger.LogInformation("DmRelayService started and events hooked.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        client.MessageReceived -= HandleMessageReceivedAsync;
        client.MessageUpdated -= HandleMessageUpdatedAsync;
        client.MessageDeleted -= HandleMessageDeletedAsync;

        logger.LogInformation("DmRelayService stopped and events unhooked.");
        return Task.CompletedTask;
    }

    // --- Event Handlers ---

    private Task HandleMessageReceivedAsync(SocketMessage message)
    {
        return Task.Run(async () =>
        {
            switch (message.Channel)
            {
                // 1. Handle Incoming DMs
                case IDMChannel when !message.Author.IsBot:
                    await ProcessIncomingDmAsync(message).ConfigureAwait(false);
                    return;
                // 2. Handle Owner Messages in Relay Channels
                case SocketTextChannel textChannel when
                    textChannel.CategoryId == _options.DmRecipientsCategory &&
                    message.Author.Id == _options.OwnerId &&
                    !message.Author.IsBot:
                    await ProcessOwnerRelayMessageAsync(message, textChannel).ConfigureAwait(false);
                    break;
            }
        });
    }

    private Task HandleMessageUpdatedAsync(Cacheable<IMessage, ulong> beforeCache, SocketMessage after,
        ISocketMessageChannel channel)
    {
        return Task.Run(async () =>
        {
            if (channel is not IDMChannel || after.Author.IsBot) return;

            logger.LogInformation("[EDITED DM] from {User} ({UserId}): {Content}", after.Author, after.Author.Id,
                after.Content);

            var webhookClient = await GetOrCreateUserRelayWebhookAsync(after.Author).ConfigureAwait(false);
            if (webhookClient == null) return;

            var before = await beforeCache.GetOrDownloadAsync().ConfigureAwait(false);

            if (before?.Content == after.Content && before.Attachments.Count == after.Attachments.Count)
            {
                logger.LogDebug(
                    "Edited DM {MessageId} from {User} had no content or attachment changes. Skipping relay.",
                    after.Id, after.Author);
                return;
            }

            var messageContent = BuildEditedMessageContent(before, after);
            var files = await AttachmentUtils.DownloadAttachmentsAsync(after.Attachments, httpClientFactory, logger)
                .ConfigureAwait(false);

            try
            {
                var relayMessageId = await webhookClient.SendFilesAsync(files, messageContent,
                        username: after.Author.Username,
                        avatarUrl: after.Author.GetDisplayAvatarUrl() ?? after.Author.GetDefaultAvatarUrl())
                    .ConfigureAwait(false);

                // Map this new edit notification message so the owner can reply to it as well
                await using var uow = await uowFactory.CreateAsync().ConfigureAwait(false);
                await uow.Users.EnsureExistsAsync(after.Author.Id).ConfigureAwait(false);
                uow.DmRelay.AddMapping(new DmRelayMappingEntity
                {
                    UserId = after.Author.Id,
                    OriginalMessageId = after.Id,
                    RelayMessageId = relayMessageId
                });
                await uow.SaveChangesAsync().ConfigureAwait(false);

                await after.AddReactionAsync(Emoji.Parse("✅")).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send edited DM relay via webhook for User {UserId}, Msg {MessageId}",
                    after.Author.Id, after.Id);
            }
            finally
            {
                AttachmentUtils.DisposeFileAttachments(files);
            }
        });
    }

    private Task HandleMessageDeletedAsync(Cacheable<IMessage, ulong> messageCache,
        Cacheable<IMessageChannel, ulong> channelCache)
    {
        return Task.Run(async () =>
        {
            var channel = channelCache.HasValue
                ? channelCache.Value
                : await channelCache.GetOrDownloadAsync().ConfigureAwait(false);
            if (channel is not IDMChannel) return;

            var message = await messageCache.GetOrDownloadAsync().ConfigureAwait(false);
            if (message == null || message.Author.IsBot) return;

            logger.LogInformation("[DELETED DM] from {User} ({UserId}): {Content}", message.Author, message.Author.Id,
                message.Content);

            var webhookClient = await GetOrCreateUserRelayWebhookAsync(message.Author).ConfigureAwait(false);
            if (webhookClient == null) return;

            var messageContent = BuildDeletedMessageContent(message);

            try
            {
                await webhookClient.SendMessageAsync(messageContent,
                        username: message.Author.Username,
                        avatarUrl: message.Author.GetDisplayAvatarUrl() ?? message.Author.GetDefaultAvatarUrl())
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send deleted DM relay via webhook for User {UserId}, Msg {MessageId}",
                    message.Author.Id, message.Id);
            }
        });
    }

    // --- Processing Logic ---

    private async Task ProcessIncomingDmAsync(SocketMessage message)
    {
        logger.LogInformation("[NEW DM] from {User} ({UserId}): {Content}", message.Author, message.Author.Id,
            message.Content);

        var webhookClient = await GetOrCreateUserRelayWebhookAsync(message.Author).ConfigureAwait(false);
        if (webhookClient == null)
        {
            await message.Channel.SendMessageAsync("Sorry, I encountered an error setting up the DM relay.")
                .ConfigureAwait(false);
            return;
        }

        var messageContent = await BuildNewMessageContentAsync(message).ConfigureAwait(false);
        var files = await AttachmentUtils.DownloadAttachmentsAsync(message.Attachments, httpClientFactory, logger)
            .ConfigureAwait(false);

        try
        {
            var relayMessageId = await webhookClient.SendFilesAsync(files, messageContent,
                    username: message.Author.Username,
                    avatarUrl: message.Author.GetDisplayAvatarUrl() ?? message.Author.GetDefaultAvatarUrl())
                .ConfigureAwait(false);

            await using var uow = await uowFactory.CreateAsync().ConfigureAwait(false);
            await uow.Users.EnsureExistsAsync(message.Author.Id).ConfigureAwait(false);

            uow.DmRelay.AddMapping(new DmRelayMappingEntity
            {
                UserId = message.Author.Id,
                OriginalMessageId = message.Id,
                RelayMessageId = relayMessageId
            });
            await uow.SaveChangesAsync().ConfigureAwait(false);

            await message.AddReactionAsync(Emoji.Parse("✅")).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send DM relay via webhook for User {UserId}, Msg {MessageId}",
                message.Author.Id, message.Id);
            try
            {
                await message.AddReactionAsync(Emoji.Parse("❌")).ConfigureAwait(false);
            }
            catch
            {
                // ignored
            }
        }
        finally
        {
            AttachmentUtils.DisposeFileAttachments(files);
        }
    }

    private async Task ProcessOwnerRelayMessageAsync(SocketMessage message, SocketTextChannel textChannel)
    {
        await using var uow = await uowFactory.CreateAsync().ConfigureAwait(false);
        var channelRecord = await uow.DmRelay.GetChannelByDiscordIdAsync(textChannel.Id).ConfigureAwait(false);

        if (channelRecord == null)
        {
            logger.LogWarning("Could not find mapped UserID for relay channel {ChannelId}", textChannel.Id);
            return;
        }

        var userId = channelRecord.UserId;
        var user = client.GetUser(userId);

        if (user == null)
        {
            logger.LogError("Failed to find user with ID {UserId} for relay from channel {ChannelId}", userId,
                textChannel.Id);
            await message.AddReactionAsync(Emoji.Parse("❓")).ConfigureAwait(false);
            return;
        }

        IDMChannel? dmChannel;
        try
        {
            dmChannel = await user.CreateDMChannelAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create DM channel for user {UserId}", userId);
            await message.AddReactionAsync(Emoji.Parse("❌")).ConfigureAwait(false);
            return;
        }

        var replyMessageReference = await GetReplyReferenceAsync(message, dmChannel).ConfigureAwait(false);
        var files = await AttachmentUtils.DownloadAttachmentsAsync(message.Attachments, httpClientFactory, logger)
            .ConfigureAwait(false);

        try
        {
            var sentDm = await dmChannel.SendFilesAsync(files, message.Content,
                embeds: message.Embeds.ToArray(),
                messageReference: replyMessageReference).ConfigureAwait(false);

            await using var uow2 = await uowFactory.CreateAsync().ConfigureAwait(false);
            uow2.DmRelay.AddMapping(new DmRelayMappingEntity
            {
                UserId = userId,
                OriginalMessageId = sentDm.Id,
                RelayMessageId = message.Id
            });
            await uow2.SaveChangesAsync().ConfigureAwait(false);

            logger.LogInformation("[DM SENT by Owner] to {User} ({UserId}): {Content}", user, user.Id,
                message.Content);
            await message.AddReactionAsync(Emoji.Parse("✅")).ConfigureAwait(false);
        }
        catch (HttpException httpEx) when (httpEx.DiscordCode == DiscordErrorCode.CannotSendMessageToUser)
        {
            logger.LogError(httpEx, "Failed to send DM to user {UserId} (User blocked bot or disabled DMs)",
                userId);
            await message.AddReactionAsync(Emoji.Parse("❌")).ConfigureAwait(false);
            await message.Channel.SendMessageAsync(
                "Failed to send DM. User might have DMs disabled or blocked the bot.",
                messageReference: message.Reference).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send owner relay DM to user {UserId}", userId);
            await message.AddReactionAsync(Emoji.Parse("❌")).ConfigureAwait(false);
        }
        finally
        {
            AttachmentUtils.DisposeFileAttachments(files);
        }
    }

    public async Task<DiscordWebhookClient?> GetOrCreateUserRelayWebhookAsync(IUser user)
    {
        var categoryId = _options.DmRecipientsCategory;

        if (client.GetChannel(categoryId) is not SocketCategoryChannel categoryChannel)
        {
            logger.LogError("DM Relay category channel {CategoryId} not found or is not a category.", categoryId);
            return null;
        }

        var guild = categoryChannel.Guild;
        var botGuildUser = guild.CurrentUser;
        if (botGuildUser == null)
        {
            logger.LogError("Bot user (guild.CurrentUser) not found in guild {GuildId}", guild.Id);
            return null;
        }

        await using var uow = await uowFactory.CreateAsync().ConfigureAwait(false);
        var channelRecord = await uow.DmRelay.GetChannelAsync(user.Id).ConfigureAwait(false);
        SocketTextChannel? targetChannel = null;

        if (channelRecord != null)
            targetChannel = categoryChannel.Channels
                .OfType<SocketTextChannel>()
                .FirstOrDefault(c => c.Id == channelRecord.ChannelId);

        if (targetChannel != null)
            return await webhookService.GetOrCreateWebhookClientAsync(targetChannel.Id).ConfigureAwait(false);

        targetChannel = await CreateRelayChannelAsync(user, categoryChannel, guild, botGuildUser, uow)
            .ConfigureAwait(false);

        if (targetChannel == null) return null;

        return await webhookService.GetOrCreateWebhookClientAsync(targetChannel.Id).ConfigureAwait(false);
    }

    private async Task<SocketTextChannel?> CreateRelayChannelAsync(IUser user, SocketCategoryChannel categoryChannel,
        SocketGuild guild, SocketGuildUser botGuildUser, IUnitOfWork uow)
    {
        if (!botGuildUser.GuildPermissions.ManageChannels)
        {
            logger.LogError(
                "Bot lacks 'Manage Channels' permission in category {CategoryName} ({CategoryId}) to create relay channel for {User} ({UserId}).",
                categoryChannel.Name, categoryChannel.Id, user.Username, user.Id);
            return null;
        }

        logger.LogInformation("Relay channel for user {User} ({UserId}) not found. Creating...", user.Username,
            user.Id);
        try
        {
            var channelName = SanitizeChannelName(user);

            var createdRestChannel = await guild.CreateTextChannelAsync(channelName, props =>
            {
                props.CategoryId = categoryChannel.Id;
                props.PermissionOverwrites = new List<Overwrite>
                {
                    new(guild.EveryoneRole.Id, PermissionTarget.Role,
                        new OverwritePermissions(viewChannel: PermValue.Deny)),
                    new(botGuildUser.Id, PermissionTarget.User,
                        new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow,
                            manageMessages: PermValue.Allow,
                            manageWebhooks: PermValue.Allow,
                            readMessageHistory: PermValue.Allow)),
                    new(_options.OwnerId, PermissionTarget.User,
                        new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow,
                            manageMessages: PermValue.Allow, readMessageHistory: PermValue.Allow,
                            manageChannel: PermValue.Allow))
                };
            }).ConfigureAwait(false);

            var targetChannel = client.GetChannel(createdRestChannel.Id) as SocketTextChannel;

            if (targetChannel == null)
            {
                await Task.Delay(1000).ConfigureAwait(false);
                targetChannel = client.GetChannel(createdRestChannel.Id) as SocketTextChannel;
                if (targetChannel == null)
                {
                    logger.LogError(
                        "Failed to find newly created relay channel {ChannelId} for user {User} ({UserId}) after creation attempt.",
                        createdRestChannel.Id, user.Username, user.Id);
                    return null;
                }
            }

            await uow.Users.EnsureExistsAsync(user.Id).ConfigureAwait(false);
            var channelRecord = await uow.DmRelay.GetChannelAsync(user.Id).ConfigureAwait(false);

            if (channelRecord == null)
                uow.DmRelay.AddChannel(new DmRelayChannelEntity { UserId = user.Id, ChannelId = targetChannel.Id });
            else
                channelRecord.ChannelId = targetChannel.Id;
            await uow.SaveChangesAsync().ConfigureAwait(false);

            logger.LogInformation("Created relay channel {ChannelName} ({ChannelId}) for user {User} ({UserId})",
                targetChannel.Name, targetChannel.Id, user.Username, user.Id);

            return targetChannel;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to create relay channel for user {User} ({UserId}) in category {CategoryId}", user.Username,
                user.Id, categoryChannel.Id);
            return null;
        }
    }

    // --- Helper Methods for Building Message Content ---

    private static string BuildEditedMessageContent(IMessage? before, SocketMessage after)
    {
        var sb = new StringBuilder();

        if (after.Reference is { MessageId.IsSpecified: true })
            sb.AppendLine($"- Replying to a message (Original ID in DM: {after.Reference.MessageId.Value})");

        if (before?.Content != null)
        {
            sb.AppendLine("- Original Message:");
            sb.AppendLine($"```{SanitizeCodeBlock(before.Content)}```");
        }

        if (after.Content != null)
        {
            sb.AppendLine("- Updated Message:");
            sb.AppendLine($"```{SanitizeCodeBlock(after.Content)}```");
            AppendUrlIfPresent(sb, after.Content);
        }

        return sb.ToString();
    }

    private static string BuildDeletedMessageContent(IMessage message)
    {
        var sb = new StringBuilder();
        sb.AppendLine("- Deleted Message:");
        sb.AppendLine($"```{SanitizeCodeBlock(message.Content ?? "*(No text content)*")}```");

        if (message.Attachments.Count != 0)
            sb.AppendLine($"- Attachments: {message.Attachments.Count} (cannot be displayed)");
        return sb.ToString();
    }

    private async Task<string> BuildNewMessageContentAsync(SocketMessage message)
    {
        var sb = new StringBuilder();

        if (message.Reference is { MessageId.IsSpecified: true })
            await AppendReferencedMessageInfoAsync(sb, message).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(message.Content)) return sb.ToString();
        sb.AppendLine("- Content:");
        sb.AppendLine($"```{SanitizeCodeBlock(message.Content)}```");
        AppendUrlIfPresent(sb, message.Content);
        return sb.ToString();
    }

    private async Task AppendReferencedMessageInfoAsync(StringBuilder sb, SocketMessage message)
    {
        if (message.Reference is not { MessageId.IsSpecified: true }) return;

        IMessage? referencedMessage = null;
        try
        {
            referencedMessage = await message.Channel.GetMessageAsync(message.Reference.MessageId.Value)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch referenced message {MessageId} for incoming DM.",
                message.Reference.MessageId.Value);
        }

        if (referencedMessage != null)
            sb.AppendLine(
                $"- Replying to: `{referencedMessage.Content.Truncate(100)}` (by: {referencedMessage.Author.Username})");
        else
            sb.AppendLine("- Replying to: *[Message not found or inaccessible]*");
    }

    private static void AppendUrlIfPresent(StringBuilder sb, string content)
    {
        var urlMatch = RegexPatterns.Url().Match(content);
        if (urlMatch.Success) sb.AppendLine($"URL: {urlMatch.Groups["url"].Value}");
    }

    private async Task<MessageReference?> GetReplyReferenceAsync(SocketMessage message, IDMChannel dmChannel)
    {
        if (message.Reference?.MessageId.IsSpecified != true) return null;

        var relayMsgId = message.Reference.MessageId.Value;

        await using var uow = await uowFactory.CreateAsync().ConfigureAwait(false);
        var mapping = await uow.DmRelay.GetMappingByRelayIdAsync(relayMsgId).ConfigureAwait(false);

        if (mapping == null) return null;

        var originalDmId = mapping.OriginalMessageId;

        logger.LogDebug("Replying to original DM {OriginalDmId} in DM channel {DmChannelId}",
            originalDmId, dmChannel.Id);

        try
        {
            await dmChannel.GetMessageAsync(originalDmId).ConfigureAwait(false);
            return new MessageReference(originalDmId, dmChannel.Id, null, false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching original DM {OriginalDmId} for reply reference.", originalDmId);
            return null;
        }
    }

    // --- Utility Methods ---

    private static string SanitizeChannelName(IUser user)
    {
        var channelName = RegexPatterns.SanitizeText().Replace(user.Username, "").ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(channelName)) channelName = $"user-{user.Id}";
        return channelName.Truncate(100);
    }

    public static string SanitizeCodeBlock(string? content) =>
        string.IsNullOrEmpty(content)
            ? string.Empty
            : content.Replace("```", "`\u200B``");
}