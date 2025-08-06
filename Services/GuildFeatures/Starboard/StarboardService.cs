using System.Net;
using Assistant.Net.Models.Starboard;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Assistant.Net.Services.GuildFeatures.Starboard;

public class StarboardService
{
    private readonly DiscordSocketClient _client;
    private readonly StarboardConfigService _configService;
    private readonly ILogger<StarboardService> _logger;
    private readonly IMongoCollection<StarredMessageModel> _starredMessagesCollection;

    public StarboardService(
        DiscordSocketClient client,
        IMongoDatabase database,
        StarboardConfigService configService,
        ILogger<StarboardService> logger)
    {
        _client = client;
        _starredMessagesCollection = database.GetCollection<StarredMessageModel>("starredMessages");
        _configService = configService;
        _logger = logger;

        _client.ReactionAdded += HandleReactionAddedAsync;
        _client.ReactionRemoved += HandleReactionRemovedAsync;
        _client.ReactionsCleared += HandleReactionsClearedAsync;
        _client.MessageDeleted += HandleMessageDeletedAsync;
        _client.MessagesBulkDeleted += HandleMessagesBulkDeletedAsync;

        EnsureIndexesAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        _logger.LogInformation("StarboardService initialized and events hooked.");
    }

    private Task HandleReactionAddedAsync(Cacheable<IUserMessage, ulong> msg, Cacheable<IMessageChannel, ulong> chan,
        SocketReaction reaction)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await ProcessReactionAddedAsync(msg, chan, reaction).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing ReactionAdded event.");
            }
        });
        return Task.CompletedTask;
    }

    private Task HandleReactionRemovedAsync(Cacheable<IUserMessage, ulong> msg, Cacheable<IMessageChannel, ulong> chan,
        SocketReaction reaction)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await ProcessReactionRemovedAsync(msg, chan, reaction).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing ReactionRemoved event.");
            }
        });
        return Task.CompletedTask;
    }

    private Task HandleReactionsClearedAsync(Cacheable<IUserMessage, ulong> msg,
        Cacheable<IMessageChannel, ulong> chan)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await ProcessReactionsClearedAsync(msg, chan).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing ReactionsCleared event.");
            }
        });
        return Task.CompletedTask;
    }

    private Task HandleMessageDeletedAsync(Cacheable<IMessage, ulong> msg, Cacheable<IMessageChannel, ulong> chan)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await ProcessMessageDeletedAsync(msg, chan).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing MessageDeleted event.");
            }
        });
        return Task.CompletedTask;
    }

    private Task HandleMessagesBulkDeletedAsync(IReadOnlyCollection<Cacheable<IMessage, ulong>> msgs,
        Cacheable<IMessageChannel, ulong> chan)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await ProcessMessagesBulkDeletedAsync(msgs, chan).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing MessagesBulkDeleted event.");
            }
        });
        return Task.CompletedTask;
    }

    private async Task EnsureIndexesAsync()
    {
        try
        {
            var indexModels = new List<CreateIndexModel<StarredMessageModel>>();
            var starboardMsgKeys = Builders<StarredMessageModel>.IndexKeys
                .Ascending(m => m.Id.GuildId)
                .Ascending(m => m.StarboardMessageId);
            indexModels.Add(new CreateIndexModel<StarredMessageModel>(
                starboardMsgKeys,
                new CreateIndexOptions { Name = "GuildId_StarboardMessageId_Sparse", Sparse = true }));

            if (indexModels.Count > 0)
            {
                await _starredMessagesCollection.Indexes.CreateManyAsync(indexModels).ConfigureAwait(false);
                _logger.LogInformation("Ensured Starboard MongoDB indexes.");
            }
            else
            {
                _logger.LogInformation("No additional Starboard MongoDB indexes needed.");
            }
        }
        catch (MongoCommandException ex) when (ex.CodeName is "IndexOptionsConflict" or "IndexKeySpecsConflict"
                                                   or "IndexAlreadyExists")
        {
            _logger.LogWarning("Starboard indexes already exist or conflict. Details: {Error}", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure Starboard MongoDB indexes.");
        }
    }

    private static FilterDefinition<StarredMessageModel> CreateIdFilter(ulong guildId, ulong originalMessageId) =>
        Builders<StarredMessageModel>.Filter.Eq(m => m.Id,
            new StarredMessageIdKey { GuildId = guildId, OriginalMessageId = originalMessageId });

    private async Task<StarredMessageModel?> GetStarredMessageEntryAsync(ulong guildId, ulong originalMessageId) =>
        await _starredMessagesCollection.Find(CreateIdFilter(guildId, originalMessageId)).FirstOrDefaultAsync()
            .ConfigureAwait(false);

    private async Task SaveStarredMessageEntryAsync(StarredMessageModel entry)
    {
        entry.LastUpdated = DateTime.UtcNow;
        await _starredMessagesCollection.ReplaceOneAsync(Builders<StarredMessageModel>.Filter.Eq(m => m.Id, entry.Id),
            entry, new ReplaceOptions { IsUpsert = true }).ConfigureAwait(false);
    }

    private async Task DeleteStarredMessageEntryAsync(ulong guildId, ulong originalMessageId) =>
        await _starredMessagesCollection.DeleteOneAsync(CreateIdFilter(guildId, originalMessageId))
            .ConfigureAwait(false);

    private async Task DeleteManyStarredMessageEntriesAsync(ulong guildId, IEnumerable<ulong> originalMessageIds)
    {
        var compositeIds = originalMessageIds.Select(msgId => new StarredMessageIdKey
            { GuildId = guildId, OriginalMessageId = msgId });
        await _starredMessagesCollection
            .DeleteManyAsync(Builders<StarredMessageModel>.Filter.In(m => m.Id, compositeIds)).ConfigureAwait(false);
    }

    private static MessageComponent BuildStarboardComponents(IMessage originalMessage, int starCount,
        string starEmoji)
    {
        var container = new ContainerBuilder()
            .WithAccentColor(Color.Gold);

        // --- Header Section with Author and Content ---
        var hasContent = !string.IsNullOrWhiteSpace(originalMessage.Content);
        container.WithSection(section =>
        {
            section.AddComponent(new TextDisplayBuilder($"## {originalMessage.Author.Mention}"));
            if (hasContent) section.AddComponent(new TextDisplayBuilder(originalMessage.Content));
            section.WithAccessory(new ThumbnailBuilder
            {
                Media = new UnfurledMediaItemProperties
                {
                    Url = originalMessage.Author.GetDisplayAvatarUrl() ?? originalMessage.Author.GetDefaultAvatarUrl()
                }
            });
        });

        // --- Attachments ---
        var imageUrls = new List<string>();
        var otherAttachments = new List<IAttachment>();

        // Collect images from embeds and attachments
        foreach (var embed in originalMessage.Embeds)
            if (embed.Image.HasValue)
                imageUrls.Add(embed.Image.Value.Url);
            else if (embed.Thumbnail.HasValue) imageUrls.Add(embed.Thumbnail.Value.Url);

        foreach (var attachment in originalMessage.Attachments)
            if (attachment.ContentType?.StartsWith("image/") == true && attachment.Height.HasValue)
                imageUrls.Add(attachment.Url);
            else
                otherAttachments.Add(attachment);

        var hasImages = imageUrls.Count != 0;
        var hasOtherAttachments = otherAttachments.Count != 0;

        if (hasImages) container.WithMediaGallery(imageUrls);

        if (hasOtherAttachments)
        {
            var attachmentsText = string.Join("\n",
                otherAttachments.Select(a => $"📄 [{a.Filename}]({a.Url})"));
            container.WithTextDisplay(new TextDisplayBuilder($"**Attachments:**\n{attachmentsText}"));
        }

        // --- Metadata and Footer Section ---
        container.WithSeparator();

        // Star count, channel name, and timestamp are secondary
        var starText = $"{starEmoji} **{starCount}** in {MentionUtils.MentionChannel(originalMessage.Channel.Id)}";
        var timeText =
            $"{TimestampTag.FormatFromDateTimeOffset(originalMessage.CreatedAt, TimestampTagStyles.Relative)}";
        container.WithTextDisplay(new TextDisplayBuilder($"{starText}  •  {timeText}"));

        container.WithActionRow(row =>
            row.WithButton("Jump to Message", style: ButtonStyle.Link, url: originalMessage.GetJumpUrl()));

        return new ComponentBuilderV2().WithContainer(container).Build();
    }


    private async Task<(bool Success, IUserMessage? Message)> ExecuteStarboardActionAsync(
        StarboardConfigModel config,
        StarredMessageModel? entry,
        Func<ITextChannel, Task<IUserMessage?>> action,
        Func<StarredMessageModel, ITextChannel, Task> updateAction,
        Func<StarredMessageModel, ITextChannel, Task> deleteAction,
        string logContext,
        bool isCreate = false, bool isUpdate = false, bool isDelete = false)
    {
        if (config.StarboardChannelId == null)
        {
            _logger.LogWarning("[{Context}] Starboard channel ID is null for guild {GuildId}.", logContext,
                config.GuildId);
            return (false, null);
        }

        var starboardChannel = _client.GetChannel(config.StarboardChannelId.Value) as ITextChannel ??
                               await _client.Rest.GetChannelAsync(config.StarboardChannelId.Value)
                                   .ConfigureAwait(false) as ITextChannel;

        if (starboardChannel == null)
        {
            _logger.LogWarning(
                "[{Context}] Starboard channel {ChannelId} not found or not a text channel for guild {GuildId}.",
                logContext, config.StarboardChannelId.Value, config.GuildId);
            if (entry == null) return (false, null);
            entry.IsPosted = false;
            entry.StarboardMessageId = null;
            await SaveStarredMessageEntryAsync(entry).ConfigureAwait(false);
            return (false, null);
        }

        var guild = _client.GetGuild(config.GuildId);
        if (guild == null)
        {
            _logger.LogError("[{Context}] Could not get guild {GuildId} from cache.", logContext, config.GuildId);
            return (false, null);
        }

        var botUser = guild.CurrentUser;
        if (botUser == null)
        {
            _logger.LogError("[{Context}] Could not get bot user in guild {GuildId}.", logContext, config.GuildId);
            return (false, null);
        }

        ChannelPermission requiredPermissions = 0;
        if (isCreate) requiredPermissions = ChannelPermission.SendMessages;
        if (isUpdate) requiredPermissions |= ChannelPermission.SendMessages;
        if (isDelete) requiredPermissions |= ChannelPermission.ManageMessages;

        var perms = botUser.GetPermissions(starboardChannel);
        if (requiredPermissions != 0 && !perms.Has(requiredPermissions))
        {
            _logger.LogWarning(
                "[{Context}] Missing permissions ({RequiredPerms}) in starboard channel {ChannelId} for guild {GuildId}.",
                logContext, requiredPermissions, starboardChannel.Id, config.GuildId);
            if (entry == null || (!isDelete && !isUpdate)) return (false, null);
            entry.IsPosted = false;
            entry.StarboardMessageId = null;
            await SaveStarredMessageEntryAsync(entry).ConfigureAwait(false);
            return (false, null);
        }

        try
        {
            if (isCreate)
            {
                var sentMessage = await action(starboardChannel).ConfigureAwait(false);
                return (sentMessage != null, sentMessage);
            }

            if (isUpdate && entry != null)
            {
                await updateAction(entry, starboardChannel).ConfigureAwait(false);
                return (true, null);
            }

            if (!isDelete || entry == null) return (false, null);
            await deleteAction(entry, starboardChannel).ConfigureAwait(false);
            return (true, null);
        }
        catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.NotFound && entry != null)
        {
            _logger.LogWarning(ex,
                "[{Context}] Starboard message {StarboardMessageId} not found in guild {GuildId}. Unmarking as posted.",
                logContext, entry.StarboardMessageId, config.GuildId);
            entry.IsPosted = false;
            entry.StarboardMessageId = null;
            await SaveStarredMessageEntryAsync(entry).ConfigureAwait(false);
            return (false, null);
        }
        catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
        {
            _logger.LogError(ex,
                "[{Context}] Forbidden to perform action in starboard channel {ChannelId} for guild {GuildId}.",
                logContext, starboardChannel.Id, config.GuildId);
            if (entry == null || (!isDelete && !isUpdate)) return (false, null);
            entry.IsPosted = false;
            entry.StarboardMessageId = null;
            await SaveStarredMessageEntryAsync(entry).ConfigureAwait(false);
            return (false, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[{Context}] Unexpected error performing action in starboard channel {ChannelId} for guild {GuildId}.",
                logContext, starboardChannel.Id, config.GuildId);
            return (false, null);
        }
    }

    private async Task CreateStarboardPostAsync(IMessage originalMessage, StarredMessageModel entry,
        StarboardConfigModel config)
    {
        var components = BuildStarboardComponents(originalMessage, entry.StarCount, config.StarEmoji);

        async Task<IUserMessage?> CreateAction(ITextChannel channel) =>
            await channel.SendMessageAsync(components: components, allowedMentions: AllowedMentions.None,
                flags: MessageFlags.ComponentsV2).ConfigureAwait(false);

        var (success, sentMessage) =
            await ExecuteStarboardActionAsync(config, entry, CreateAction, null!, null!, "CreateStarboardPost", true)
                .ConfigureAwait(false);

        if (success && sentMessage != null)
        {
            entry.StarboardMessageId = sentMessage.Id;
            entry.IsPosted = true;
            await SaveStarredMessageEntryAsync(entry).ConfigureAwait(false);
            _logger.LogInformation(
                "[CreateStarboardPost] Posted message {OriginalMessageId} to starboard in guild {GuildId}",
                entry.Id.OriginalMessageId, config.GuildId);
        }
        else
        {
            _logger.LogError(
                "[CreateStarboardPost] Failed to send starboard message for {OriginalMessageId} in guild {GuildId}.",
                entry.Id.OriginalMessageId, config.GuildId);
        }
    }

    private async Task UpdateStarboardPostAsync(StarredMessageModel entry, StarboardConfigModel config,
        IMessage? originalMessage = null)
    {
        if (entry.StarboardMessageId == null) return;

        var (success, _) =
            await ExecuteStarboardActionAsync(config, entry, null!, UpdateAction, null!, "UpdateStarboardPost",
                isUpdate: true).ConfigureAwait(false);
        if (success)
            await SaveStarredMessageEntryAsync(entry).ConfigureAwait(false);
        return;

        async Task UpdateAction(StarredMessageModel currentEntry, ITextChannel channel)
        {
            var resolvedOriginalMessage = originalMessage;
            if (resolvedOriginalMessage == null)
                if (_client.GetChannel(currentEntry.OriginalChannelId) is ITextChannel originalChannel)
                    resolvedOriginalMessage = await originalChannel.GetMessageAsync(currentEntry.Id.OriginalMessageId)
                        .ConfigureAwait(false);
                else
                    return;

            if (resolvedOriginalMessage == null) return;

            if (await channel.GetMessageAsync(currentEntry.StarboardMessageId!.Value).ConfigureAwait(false) is not
                IUserMessage starboardMsg)
            {
                _logger.LogWarning(
                    "[UpdateStarboardPost] Starboard message {StarboardMessageId} became null during fetch for guild {GuildId}.",
                    currentEntry.StarboardMessageId, config.GuildId);
                currentEntry.IsPosted = false;
                currentEntry.StarboardMessageId = null;
                throw new HttpException(HttpStatusCode.NotFound, null);
            }

            var newComponents =
                BuildStarboardComponents(resolvedOriginalMessage, currentEntry.StarCount, config.StarEmoji);
            await starboardMsg.ModifyAsync(props =>
            {
                props.Content = "";
                props.Components = newComponents;
            }).ConfigureAwait(false);

            _logger.LogDebug(
                "[UpdateStarboardPost] Updated components for starboard message {StarboardMessageId} in guild {GuildId} to {StarCount} stars",
                currentEntry.StarboardMessageId, config.GuildId, currentEntry.StarCount);
        }
    }

    private async Task DeleteStarboardPostAsync(StarredMessageModel entry, StarboardConfigModel config)
    {
        if (entry.StarboardMessageId == null)
        {
            if (!entry.IsPosted) return;
            entry.IsPosted = false;
            await SaveStarredMessageEntryAsync(entry).ConfigureAwait(false);

            return;
        }

        var (success, _) =
            await ExecuteStarboardActionAsync(config, entry, null!, null!, DeleteAction, "DeleteStarboardPost",
                isDelete: true).ConfigureAwait(false);

        if (entry.IsPosted || entry.StarboardMessageId != null)
        {
            entry.IsPosted = false;
            entry.StarboardMessageId = null;
            await SaveStarredMessageEntryAsync(entry).ConfigureAwait(false);
        }

        if (success)
            _logger.LogInformation(
                "[DeleteStarboardPost] Deleted starboard message {StarboardMessageId} for original {OriginalMessageId} in guild {GuildId}",
                entry.StarboardMessageId, entry.Id.OriginalMessageId, config.GuildId);
        return;

        async Task DeleteAction(StarredMessageModel currentEntry, ITextChannel channel) =>
            await channel.DeleteMessageAsync(currentEntry.StarboardMessageId!.Value).ConfigureAwait(false);
    }

    private async Task ProcessReactionAddedAsync(Cacheable<IUserMessage, ulong> messageCache,
        Cacheable<IMessageChannel, ulong> channelCache, SocketReaction reaction)
    {
        IMessageChannel? channel;
        if (channelCache.HasValue) channel = channelCache.Value;
        else
            try
            {
                channel = await channelCache.GetOrDownloadAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ReactionAdded] Failed to get channel {ChannelId}", channelCache.Id);
                return;
            }

        if (channel is not SocketGuildChannel guildChannel) return;
        var guildId = guildChannel.Guild.Id;

        if (reaction.UserId == _client.CurrentUser.Id) return;
        var user = reaction.User.IsSpecified ? reaction.User.Value : _client.GetUser(reaction.UserId);
        if (user == null || user.IsBot) return;

        var config = await _configService.GetGuildConfigAsync(guildId).ConfigureAwait(false);
        if (!config.IsEnabled || config.StarboardChannelId == null || reaction.Emote.ToString() != config.StarEmoji ||
            guildChannel.Id == config.StarboardChannelId.Value) return;

        var originalMessage = await messageCache.GetOrDownloadAsync().ConfigureAwait(false);
        if (originalMessage == null)
        {
            _logger.LogDebug("[ReactionAdded] Original message {MessageId} not found.", messageCache.Id);
            await DeleteStarredMessageEntryAsync(guildId, messageCache.Id).ConfigureAwait(false);
            return;
        }

        if (!config.AllowBotMessages && originalMessage.Author.IsBot) return;
        if (!config.AllowSelfStar && reaction.UserId == originalMessage.Author.Id)
        {
            if (guildChannel.Guild.CurrentUser.GetPermissions(guildChannel).ManageMessages)
                try
                {
                    await originalMessage.RemoveReactionAsync(reaction.Emote, user).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[ReactionAdded] Failed to remove self-star by {UserId} on {MessageId}",
                        user.Id, originalMessage.Id);
                }
            else
                _logger.LogDebug(
                    "[ReactionAdded] Bot lacks permission to remove self-star by {UserId} on {MessageId} in {ChannelId}",
                    user.Id, originalMessage.Id, guildChannel.Id);

            return;
        }

        if (config.IgnoreNsfwChannels && guildChannel is ITextChannel { IsNsfw: true }) return;

        var entry = await GetStarredMessageEntryAsync(guildId, originalMessage.Id).ConfigureAwait(false) ??
                    new StarredMessageModel
                    {
                        Id = new StarredMessageIdKey { GuildId = guildId, OriginalMessageId = originalMessage.Id },
                        OriginalChannelId = guildChannel.Id,
                        StarrerUserIds = new HashSet<ulong>()
                    };

        if (entry.StarrerUserIds.Add(reaction.UserId))
        {
            entry.StarCount = entry.StarrerUserIds.Count;
            await SaveStarredMessageEntryAsync(entry).ConfigureAwait(false);

            if (entry.IsPosted)
                await UpdateStarboardPostAsync(entry, config, originalMessage).ConfigureAwait(false);
            else if (entry.StarCount >= config.Threshold)
                await CreateStarboardPostAsync(originalMessage, entry, config).ConfigureAwait(false);
        }
        else
        {
            _logger.LogDebug("[ReactionAdded] User {UserId} already starred {MessageId}.", reaction.UserId,
                originalMessage.Id);
        }
    }

    private async Task ProcessReactionRemovedAsync(Cacheable<IUserMessage, ulong> messageCache,
        Cacheable<IMessageChannel, ulong> channelCache, SocketReaction reaction)
    {
        IMessageChannel? channel;
        if (channelCache.HasValue) channel = channelCache.Value;
        else
            try
            {
                channel = await channelCache.GetOrDownloadAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ReactionRemoved] Failed to get channel {ChannelId}", channelCache.Id);
                return;
            }

        if (channel is not SocketGuildChannel guildChannel) return;
        var guildId = guildChannel.Guild.Id;

        var config = await _configService.GetGuildConfigAsync(guildId).ConfigureAwait(false);
        if (config.StarboardChannelId == null || reaction.Emote.ToString() != config.StarEmoji ||
            guildChannel.Id == config.StarboardChannelId.Value) return;

        var messageId = messageCache.Id;
        var entry = await GetStarredMessageEntryAsync(guildId, messageId).ConfigureAwait(false);
        if (entry == null) return;

        if (entry.StarrerUserIds.Remove(reaction.UserId))
        {
            entry.StarCount = entry.StarrerUserIds.Count;
            if (entry.IsPosted)
            {
                if (entry.StarCount < config.Threshold && config.DeleteIfUnStarred)
                {
                    await DeleteStarboardPostAsync(entry, config).ConfigureAwait(false);
                }
                else
                {
                    var originalMessage = await messageCache.GetOrDownloadAsync().ConfigureAwait(false);
                    await UpdateStarboardPostAsync(entry, config, originalMessage).ConfigureAwait(false);
                }
            }

            await SaveStarredMessageEntryAsync(entry).ConfigureAwait(false);
        }
        else
        {
            _logger.LogDebug("[ReactionRemoved] User {UserId} hadn't starred {MessageId}.", reaction.UserId, messageId);
        }
    }

    private async Task ProcessReactionsClearedAsync(Cacheable<IUserMessage, ulong> messageCache,
        Cacheable<IMessageChannel, ulong> channelCache)
    {
        IMessageChannel? channel;
        if (channelCache.HasValue) channel = channelCache.Value;
        else
            try
            {
                channel = await channelCache.GetOrDownloadAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ReactionsCleared] Failed to get channel {ChannelId}", channelCache.Id);
                return;
            }

        if (channel is not IGuildChannel guildChannel) return;
        var guildId = guildChannel.GuildId;

        var config = await _configService.GetGuildConfigAsync(guildId).ConfigureAwait(false);
        if (config.StarboardChannelId == null) return;

        var messageId = messageCache.Id;
        var entry = await GetStarredMessageEntryAsync(guildId, messageId).ConfigureAwait(false);
        if (entry == null) return;

        entry.StarrerUserIds.Clear();
        entry.StarCount = 0;

        if (entry.IsPosted)
        {
            if (config.DeleteIfUnStarred)
            {
                await DeleteStarboardPostAsync(entry, config).ConfigureAwait(false);
            }
            else
            {
                var originalMessage = await messageCache.GetOrDownloadAsync().ConfigureAwait(false);
                await UpdateStarboardPostAsync(entry, config, originalMessage).ConfigureAwait(false);
            }
        }

        await SaveStarredMessageEntryAsync(entry).ConfigureAwait(false);
        _logger.LogInformation("[ReactionsCleared] Cleared stars on message {MessageId} in guild {GuildId}", messageId,
            guildId);
    }

    private async Task ProcessMessageDeletedAsync(Cacheable<IMessage, ulong> messageCache,
        Cacheable<IMessageChannel, ulong> channelCache)
    {
        IMessageChannel? channel;
        if (channelCache.HasValue) channel = channelCache.Value;
        else
            try
            {
                channel = await channelCache.GetOrDownloadAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[MessageDeleted] Failed to get channel {ChannelId}", channelCache.Id);
                return;
            }

        if (channel is not IGuildChannel guildChannel) return;
        var guildId = guildChannel.GuildId;

        var config = await _configService.GetGuildConfigAsync(guildId).ConfigureAwait(false);

        var messageId = messageCache.Id;
        var entry = await GetStarredMessageEntryAsync(guildId, messageId).ConfigureAwait(false);

        if (entry is { IsPosted: true } &&
            config.StarboardChannelId.HasValue)
            await DeleteStarboardPostAsync(entry, config).ConfigureAwait(false);

        if (entry != null)
        {
            await DeleteStarredMessageEntryAsync(guildId, messageId).ConfigureAwait(false);
            _logger.LogDebug("[MessageDeleted] Deleted DB entry for {MessageId} in {GuildId}", messageId, guildId);
        }
    }

    private async Task ProcessMessagesBulkDeletedAsync(IReadOnlyCollection<Cacheable<IMessage, ulong>> messageCaches,
        Cacheable<IMessageChannel, ulong> channelCache)
    {
        IMessageChannel? channel;
        if (channelCache.HasValue) channel = channelCache.Value;
        else
            try
            {
                channel = await channelCache.GetOrDownloadAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[BulkDelete] Failed to get channel {ChannelId}", channelCache.Id);
                return;
            }

        if (channel is not IGuildChannel guildChannel) return;
        var guildId = guildChannel.GuildId;

        var config = await _configService.GetGuildConfigAsync(guildId).ConfigureAwait(false);

        var messageIds = messageCaches.Select(mc => mc.Id).ToList();
        if (messageIds.Count == 0) return;

        if (config.StarboardChannelId.HasValue)
        {
            var compositeIds = messageIds.Select(msgId => new StarredMessageIdKey
                { GuildId = guildId, OriginalMessageId = msgId });
            var filter = Builders<StarredMessageModel>.Filter.In(m => m.Id, compositeIds) &
                         Builders<StarredMessageModel>.Filter.Eq(m => m.IsPosted, true);
            var entriesToDeleteSbMsg =
                await _starredMessagesCollection.Find(filter).ToListAsync().ConfigureAwait(false);

            if (entriesToDeleteSbMsg.Count != 0)
            {
                var deleteTasks = entriesToDeleteSbMsg.Select(entry => DeleteStarboardPostAsync(entry, config))
                    .ToList();
                try
                {
                    await Task.WhenAll(deleteTasks).ConfigureAwait(false);
                    _logger.LogInformation(
                        "[BulkDelete] Attempted deletion of {Count} starboard posts for bulk delete in {GuildId}",
                        deleteTasks.Count, guildId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[BulkDelete] Error during bulk deletion of starboard posts for {GuildId}",
                        guildId);
                }
            }
        }

        await DeleteManyStarredMessageEntriesAsync(guildId, messageIds).ConfigureAwait(false);
        _logger.LogInformation("[BulkDelete] Cleaned DB for {Count} bulk deleted messages in {GuildId}",
            messageIds.Count, guildId);
    }
}