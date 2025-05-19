using System.Text;
using Assistant.Net.Configuration;
using Assistant.Net.Utilities;
using Discord;
using Discord.Net;
using Discord.Webhook;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Services;

public class DmRelayService
{
    private const string ChannelTopicPrefix = "USERID:";
    private const string MessageIdPrefix = "MSGID:";

    private readonly DiscordSocketClient _client;
    private readonly Config _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DmRelayService> _logger;
    private readonly WebhookService _webhookService;

    public DmRelayService(
        DiscordSocketClient client,
        Config config,
        ILogger<DmRelayService> logger,
        IHttpClientFactory httpClientFactory,
        WebhookService webhookService)
    {
        _client = client;
        _config = config;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _webhookService = webhookService;

        _client.MessageReceived += HandleMessageReceivedAsync;
        _client.MessageUpdated += HandleMessageUpdatedAsync;
        _client.MessageDeleted += HandleMessageDeletedAsync;

        _logger.LogInformation("DmRelayService initialized and events hooked.");
    }
    // --- Event Handlers ---

    private async Task HandleMessageReceivedAsync(SocketMessage message)
    {
        switch (message.Channel)
        {
            // 1. Handle Incoming DMs
            case IDMChannel when !message.Author.IsBot:
                await ProcessIncomingDmAsync(message);
                return;
            // 2. Handle Owner Messages in Relay Channels
            case SocketTextChannel textChannel when
                textChannel.CategoryId == _config.Client.DmRecipientsCategory &&
                message.Author.Id == _config.Client.OwnerId &&
                !message.Author.IsBot:
                await ProcessOwnerRelayMessageAsync(message, textChannel);
                break;
        }
    }

    private async Task HandleMessageUpdatedAsync(Cacheable<IMessage, ulong> beforeCache, SocketMessage after,
        ISocketMessageChannel channel)
    {
        if (channel is not IDMChannel || after.Author.IsBot) return;

        _logger.LogInformation("[EDITED DM] from {User} ({UserId}): {Content}", after.Author, after.Author.Id,
            after.Content);

        var webhookClient = await GetOrCreateUserRelayWebhookAsync(after.Author);
        if (webhookClient == null) return;

        var before = await beforeCache.GetOrDownloadAsync();

        var messageContent = BuildEditedMessageContent(before, after);
        var files = await AttachmentUtils.DownloadAttachmentsAsync(after.Attachments, _httpClientFactory, _logger);

        try
        {
            await webhookClient.SendFilesAsync(files, messageContent,
                username: after.Author.Username,
                avatarUrl: after.Author.GetDisplayAvatarUrl() ?? after.Author.GetDefaultAvatarUrl());
            await after.AddReactionAsync(Emoji.Parse("✅"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send edited DM relay via webhook for User {UserId}, Msg {MessageId}",
                after.Author.Id, after.Id);
        }
        finally
        {
            AttachmentUtils.DisposeFileAttachments(files);
        }
    }

    private async Task HandleMessageDeletedAsync(Cacheable<IMessage, ulong> messageCache,
        Cacheable<IMessageChannel, ulong> channelCache)
    {
        // Ensure the channel is a DM channel before proceeding
        var channel = channelCache.HasValue ? channelCache.Value : await channelCache.GetOrDownloadAsync();
        if (channel is not IDMChannel) return;

        var message = await messageCache.GetOrDownloadAsync();
        // Don't relay deletion notices for bot messages or if message data is unavailable
        if (message == null || message.Author.IsBot) return;

        _logger.LogInformation("[DELETED DM] from {User} ({UserId}): {Content}", message.Author, message.Author.Id,
            message.Content);

        var webhookClient = await GetOrCreateUserRelayWebhookAsync(message.Author);
        if (webhookClient == null) return;

        var messageContent = BuildDeletedMessageContent(message);

        try
        {
            await webhookClient.SendMessageAsync(messageContent,
                username: message.Author.Username,
                avatarUrl: message.Author.GetDisplayAvatarUrl() ?? message.Author.GetDefaultAvatarUrl());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send deleted DM relay via webhook for User {UserId}, Msg {MessageId}",
                message.Author.Id, message.Id);
        }
    }

    // --- Processing Logic ---
    private async Task ProcessIncomingDmAsync(SocketMessage message)
    {
        _logger.LogInformation("[NEW DM] from {User} ({UserId}): {Content}", message.Author, message.Author.Id,
            message.Content);

        var webhookClient = await GetOrCreateUserRelayWebhookAsync(message.Author); // Changed to use helper
        if (webhookClient == null)
        {
            await message.Channel.SendMessageAsync("Sorry, I encountered an error setting up the DM relay.");
            return;
        }

        var messageContent = await BuildNewMessageContentAsync(message);
        var files = await AttachmentUtils.DownloadAttachmentsAsync(message.Attachments, _httpClientFactory, _logger);

        try
        {
            await webhookClient.SendFilesAsync(files, messageContent,
                username: message.Author.Username,
                avatarUrl: message.Author.GetDisplayAvatarUrl() ?? message.Author.GetDefaultAvatarUrl());
            await message.AddReactionAsync(Emoji.Parse("✅"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send DM relay via webhook for User {UserId}, Msg {MessageId}",
                message.Author.Id, message.Id);
            try
            {
                await message.AddReactionAsync(Emoji.Parse("❌"));
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
        var userId = ExtractUserIdFromTopic(textChannel.Topic);
        if (userId == null)
        {
            _logger.LogWarning("Could not parse UserID from topic '{Topic}' in relay channel {ChannelId}",
                textChannel.Topic, textChannel.Id);
            return;
        }

        var user = _client.GetUser(userId.Value);
        if (user == null)
        {
            _logger.LogError("Failed to find user with ID {UserId} for relay from channel {ChannelId}", userId.Value,
                textChannel.Id);
            await message.AddReactionAsync(Emoji.Parse("❓"));
            return;
        }

        IDMChannel? dmChannel;
        try
        {
            dmChannel = await user.CreateDMChannelAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create DM channel for user {UserId}", userId.Value);
            await message.AddReactionAsync(Emoji.Parse("❌"));
            return;
        }

        // Check for reply reference
        var replyMessageReference = await GetReplyReferenceAsync(message, textChannel, dmChannel);
        var files = await AttachmentUtils.DownloadAttachmentsAsync(message.Attachments, _httpClientFactory, _logger);

        try
        {
            await dmChannel.SendFilesAsync(files, message.Content,
                embeds: message.Embeds.ToArray(),
                messageReference: replyMessageReference);

            _logger.LogInformation("[DM SENT by Owner] to {User} ({UserId}): {Content}", user, user.Id,
                message.Content);
            await message.AddReactionAsync(Emoji.Parse("✅"));
        }
        catch (HttpException httpEx) when (httpEx.DiscordCode == DiscordErrorCode.CannotSendMessageToUser)
        {
            _logger.LogError(httpEx, "Failed to send DM to user {UserId} (User blocked bot or disabled DMs)",
                userId.Value);
            await message.AddReactionAsync(Emoji.Parse("❌"));
            await message.Channel.SendMessageAsync(
                "Failed to send DM. User might have DMs disabled or blocked the bot.",
                messageReference: message.Reference);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send owner relay DM to user {UserId}", userId.Value);
            await message.AddReactionAsync(Emoji.Parse("❌"));
        }
        finally
        {
            AttachmentUtils.DisposeFileAttachments(files);
        }
    }

    // --- Webhook Management (Specific to DM Relay Channel Creation) ---
    /// <summary>
    ///     Gets or creates the specific relay channel for a user and then gets/creates the webhook for that channel.
    /// </summary>
    public async Task<DiscordWebhookClient?> GetOrCreateUserRelayWebhookAsync(IUser user)
    {
        var categoryId = _config.Client.DmRecipientsCategory;

        if (_client.GetChannel(categoryId) is not SocketCategoryChannel categoryChannel)
        {
            _logger.LogError("DM Relay category channel {CategoryId} not found or is not a category.", categoryId);
            return null;
        }

        var guild = categoryChannel.Guild;
        var botGuildUser = guild.CurrentUser;
        if (botGuildUser == null)
        {
            _logger.LogError("Bot user (guild.CurrentUser) not found in guild {GuildId}", guild.Id);
            return null;
        }

        // Find or create the user-specific channel
        var userTopic = $"{ChannelTopicPrefix}{user.Id}";
        var targetChannel = categoryChannel.Channels
            .OfType<SocketTextChannel>()
            .FirstOrDefault(c => c.Topic == userTopic);

        if (targetChannel == null)
        {
            targetChannel = await CreateRelayChannelAsync(user, categoryChannel, guild, botGuildUser, userTopic);
            if (targetChannel == null) return null; // Failed to create channel
        }

        return await _webhookService.GetOrCreateWebhookClientAsync(targetChannel.Id);
    }

    private async Task<SocketTextChannel?> CreateRelayChannelAsync(IUser user, SocketCategoryChannel categoryChannel,
        SocketGuild guild, IGuildUser botGuildUser, string userTopic)
    {
        if (!botGuildUser.GuildPermissions.ManageChannels)
        {
            _logger.LogError(
                "Bot lacks 'Manage Channels' permission in category {CategoryName} ({CategoryId}) to create relay channel for {User} ({UserId}).",
                categoryChannel.Name, categoryChannel.Id, user.Username, user.Id);
            return null;
        }

        // ManageWebhooks permission is checked by WebhookService for the target channel.
        // Here, we only need ManageChannels for the category.

        _logger.LogInformation("Relay channel for user {User} ({UserId}) not found. Creating...", user.Username,
            user.Id);
        try
        {
            var channelName = SanitizeChannelName(user);

            var createdRestChannel = await guild.CreateTextChannelAsync(channelName, props =>
            {
                props.CategoryId = categoryChannel.Id;
                props.Topic = userTopic;
                props.PermissionOverwrites = new List<Overwrite>
                {
                    new(guild.EveryoneRole.Id, PermissionTarget.Role,
                        new OverwritePermissions(viewChannel: PermValue.Deny)),
                    new(botGuildUser.Id, PermissionTarget.User,
                        new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow,
                            manageMessages: PermValue.Allow,
                            manageWebhooks: PermValue.Allow, // Bot needs manage webhooks here
                            readMessageHistory: PermValue.Allow)),
                    new(_config.Client.OwnerId!.Value, PermissionTarget.User, // Assuming OwnerId is configured
                        new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow,
                            manageMessages: PermValue.Allow, readMessageHistory: PermValue.Allow,
                            manageChannel: PermValue.Allow))
                };
            });

            // Fetch the SocketTextChannel instance after creation
            var targetChannel = _client.GetChannel(createdRestChannel.Id) as SocketTextChannel;

            if (targetChannel == null)
            {
                await Task.Delay(1000); // Wait a moment for cache to update
                targetChannel = _client.GetChannel(createdRestChannel.Id) as SocketTextChannel;
                if (targetChannel == null)
                {
                    _logger.LogError(
                        "Failed to find newly created relay channel {ChannelId} for user {User} ({UserId}) after creation attempt.",
                        createdRestChannel.Id, user.Username, user.Id);
                    return null;
                }
            }

            _logger.LogInformation("Created relay channel {ChannelName} ({ChannelId}) for user {User} ({UserId})",
                targetChannel.Name, targetChannel.Id, user.Username, user.Id);

            return targetChannel;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to create relay channel for user {User} ({UserId}) in category {CategoryId}", user.Username,
                user.Id, categoryChannel.Id);
            return null;
        }
    }
    // --- Helper Methods for Building Message Content ---

    private string BuildEditedMessageContent(IMessage? before, SocketMessage after)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{MessageIdPrefix}{after.Id}");
        sb.AppendLine();

        if (after.Reference is { MessageId.IsSpecified: true })
            // For simplicity, we won't resolve the replied-to message content here in the log.
            // The owner can see the reply context in the relay channel itself.
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

        sb.AppendLine("----------");
        return sb.ToString();
    }

    private static string BuildDeletedMessageContent(IMessage message)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"- Deleted Message (ID: {message.Id}):");
        sb.AppendLine($"```{SanitizeCodeBlock(message.Content ?? "*(No text content)*")}```");

        if (message.Attachments.Count != 0)
            sb.AppendLine($"- Attachments: {message.Attachments.Count} (cannot be displayed)");

        sb.AppendLine("----------");
        return sb.ToString();
    }

    private async Task<string> BuildNewMessageContentAsync(SocketMessage message)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{MessageIdPrefix}{message.Id}");
        sb.AppendLine();

        if (message.Reference is { MessageId.IsSpecified: true }) await AppendReferencedMessageInfoAsync(sb, message);

        if (!string.IsNullOrWhiteSpace(message.Content))
        {
            sb.AppendLine("- Content:");
            sb.AppendLine($"```{SanitizeCodeBlock(message.Content)}```");
            AppendUrlIfPresent(sb, message.Content);
        }

        sb.AppendLine("----------");
        return sb.ToString();
    }

    private async Task AppendReferencedMessageInfoAsync(StringBuilder sb, SocketMessage message)
    {
        if (message.Reference is not { MessageId.IsSpecified: true }) return;

        IMessage? referencedMessage = null;
        try
        {
            // Try fetching from the DM channel context
            referencedMessage = await message.Channel.GetMessageAsync(message.Reference.MessageId.Value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch referenced message {MessageId} for incoming DM.",
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

    private async Task<MessageReference?> GetReplyReferenceAsync(SocketMessage message, SocketTextChannel textChannel,
        IDMChannel dmChannel)
    {
        if (message.Reference?.MessageId.IsSpecified != true) return null;

        var referencedWebhookMsg = await textChannel.GetMessageAsync(message.Reference.MessageId.Value);
        if (referencedWebhookMsg == null || !referencedWebhookMsg.Author.IsWebhook) return null;

        var originalDmId = ExtractMessageIdFromWebhookContent(referencedWebhookMsg.Content);
        if (!originalDmId.HasValue) return null;

        _logger.LogDebug("Replying to original DM {OriginalDmId} in DM channel {DmChannelId}",
            originalDmId.Value, dmChannel.Id);

        // Verify the message exists in the DM channel before creating a reference
        try
        {
            await dmChannel.GetMessageAsync(originalDmId.Value);
            return new MessageReference(originalDmId.Value, dmChannel.Id, null, false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching original DM {OriginalDmId} for reply reference.", originalDmId.Value);
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

    private static ulong? ExtractUserIdFromTopic(string? topic)
    {
        if (string.IsNullOrEmpty(topic) || !topic.StartsWith(ChannelTopicPrefix)) return null;

        return ulong.TryParse(topic.AsSpan(ChannelTopicPrefix.Length), out var userId) ? userId : null;
    }

    private static ulong? ExtractMessageIdFromWebhookContent(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;

        var firstLine = content.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (firstLine == null || !firstLine.StartsWith(MessageIdPrefix)) return null;

        return ulong.TryParse(firstLine.AsSpan(MessageIdPrefix.Length), out var messageId) ? messageId : null;
    }

    public static string SanitizeCodeBlock(string? content) =>
        string.IsNullOrEmpty(content)
            ? string.Empty
            : content.Replace("```", "`\u200B``");
}