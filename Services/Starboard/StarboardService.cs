using System.Net;
using Assistant.Net.Models.Starboard;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Assistant.Net.Services.Starboard;

public class StarboardService
{
    private const string StarContentFormat = "{emoji} **{count}** | {channelMention}";

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

        // Hook events internally
        _client.ReactionAdded += HandleReactionAddedAsync;
        _client.ReactionRemoved += HandleReactionRemovedAsync;
        _client.ReactionsCleared += HandleReactionsClearedAsync;
        _client.MessageDeleted += HandleMessageDeletedAsync;
        _client.MessagesBulkDeleted += HandleMessagesBulkDeletedAsync;


        // Run index creation synchronously during startup. TODO: Consider async if startup time is critical.
        EnsureIndexesAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        _logger.LogInformation("StarboardService initialized and events hooked.");
    }

    // --- Event Wrappers ---
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

    private Task HandleReactionsClearedAsync(Cacheable<IUserMessage, ulong> msg, Cacheable<IMessageChannel, ulong> chan)
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


    // --- Index Creation ---
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
                _logger.LogInformation(
                    "No additional Starboard MongoDB indexes needed (primary key covers uniqueness).");
            }
        }
        catch (MongoCommandException ex) when (ex.CodeName == "IndexOptionsConflict" ||
                                               ex.CodeName == "IndexKeySpecsConflict" ||
                                               ex.Message.Contains("already exists with different options"))
        {
            _logger.LogWarning(
                "Starboard indexes already exist with different options or keys. This might be okay if definitions match. Details: {Error}",
                ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure Starboard MongoDB indexes.");
        }
    }

    // --- Database Operations ---

    private static FilterDefinition<StarredMessageModel> CreateIdFilter(ulong guildId, ulong originalMessageId)
    {
        var compositeId = new StarredMessageIdKey { GuildId = guildId, OriginalMessageId = originalMessageId };
        return Builders<StarredMessageModel>.Filter.Eq(m => m.Id, compositeId);
    }

    private async Task<StarredMessageModel?> GetStarredMessageEntryAsync(ulong guildId, ulong originalMessageId)
    {
        var filter = CreateIdFilter(guildId, originalMessageId);
        return await _starredMessagesCollection.Find(filter).FirstOrDefaultAsync().ConfigureAwait(false);
    }

    private async Task SaveStarredMessageEntryAsync(StarredMessageModel entry)
    {
        entry.LastUpdated = DateTime.UtcNow;
        var filter = Builders<StarredMessageModel>.Filter.Eq(m => m.Id, entry.Id);
        var options = new ReplaceOptions { IsUpsert = true };
        await _starredMessagesCollection.ReplaceOneAsync(filter, entry, options).ConfigureAwait(false);
    }

    private async Task DeleteStarredMessageEntryAsync(ulong guildId, ulong originalMessageId)
    {
        var filter = CreateIdFilter(guildId, originalMessageId);
        await _starredMessagesCollection.DeleteOneAsync(filter).ConfigureAwait(false);
    }

    private async Task DeleteManyStarredMessageEntriesAsync(ulong guildId, IEnumerable<ulong> originalMessageIds)
    {
        var compositeIds = originalMessageIds.Select(msgId => new StarredMessageIdKey
            { GuildId = guildId, OriginalMessageId = msgId });
        var filter = Builders<StarredMessageModel>.Filter.In(m => m.Id, compositeIds);
        await _starredMessagesCollection.DeleteManyAsync(filter).ConfigureAwait(false);
    }

    // --- Helper Functions ---

    private static Embed CreateStarboardEmbed(IMessage originalMessage)
    {
        var embedBuilder = new EmbedBuilder()
            .WithDescription(originalMessage.Content)
            .WithColor(Color.Gold)
            .WithTimestamp(originalMessage.CreatedAt)
            .WithAuthor(originalMessage.Author.GlobalName ?? originalMessage.Author.Username,
                originalMessage.Author.GetDisplayAvatarUrl() ?? originalMessage.Author.GetDefaultAvatarUrl(),
                originalMessage.GetJumpUrl())
            .AddField("Original Message", $"[Jump!]({originalMessage.GetJumpUrl()})");

        var imageSet = false;
        var attachmentList = new List<string>();

        if (originalMessage.Embeds.Count > 0)
        {
            var firstEmbed = originalMessage.Embeds.First();
            if (!string.IsNullOrEmpty(firstEmbed.Image?.Url))
            {
                embedBuilder.WithImageUrl(firstEmbed.Image.Value.Url);
                imageSet = true;
            }
            else if (!string.IsNullOrEmpty(firstEmbed.Thumbnail?.Url))
            {
                embedBuilder.WithThumbnailUrl(firstEmbed.Thumbnail.Value.Url);
            }
        }

        foreach (var attachment in originalMessage.Attachments)
            if (!imageSet && attachment.ContentType?.StartsWith("image/") == true &&
                attachment.Height.HasValue)
            {
                embedBuilder.WithImageUrl(attachment.Url);
                imageSet = true;
            }
            else
            {
                attachmentList.Add($"[{attachment.Filename}]({attachment.Url})");
            }

        if (attachmentList.Count > 0) embedBuilder.AddField("Attachments", string.Join("\n", attachmentList));

        return embedBuilder.Build();
    }

    private async Task CreateStarboardPostAsync(IMessage originalMessage, StarredMessageModel entry,
        StarboardConfigModel config)
    {
        var guildId = entry.Id.GuildId;
        if (config.StarboardChannelId == null) return;

        var starboardChannel = _client.GetChannel(config.StarboardChannelId.Value) as ITextChannel ??
                               await _client.Rest.GetChannelAsync(config.StarboardChannelId.Value).ConfigureAwait(false) as ITextChannel;
        if (starboardChannel == null)
        {
            _logger.LogWarning(
                "[CreateStarboardPost] Starboard channel {ChannelId} not found or not a text channel for guild {GuildId}.",
                config.StarboardChannelId.Value, guildId);
            return;
        }

        var guild = _client.GetGuild(guildId);
        if (guild == null)
        {
            _logger.LogError("[CreateStarboardPost] Could not get guild {GuildId} from cache.", guildId);
            return;
        }

        var botUser = guild.CurrentUser;
        if (botUser == null)
        {
            _logger.LogError("[CreateStarboardPost] Could not get bot user (guild.CurrentUser) in guild {GuildId}.",
                guildId);
            return;
        }

        var perms = botUser.GetPermissions(starboardChannel);
        if (!perms.SendMessages || !perms.EmbedLinks)
        {
            _logger.LogWarning(
                "[CreateStarboardPost] Missing Send/Embed permissions in starboard channel {ChannelId} for guild {GuildId}.",
                starboardChannel.Id, guildId);
            return;
        }

        var content = StarContentFormat.Replace("{emoji}", config.StarEmoji)
            .Replace("{count}", entry.StarCount.ToString())
            .Replace("{channelMention}", $"<#{originalMessage.Channel.Id}>");
        var embed = CreateStarboardEmbed(originalMessage);

        try
        {
            var starboardMsg =
                await starboardChannel.SendMessageAsync(content, embed: embed,
                    allowedMentions: AllowedMentions.None).ConfigureAwait(false);
            entry.StarboardMessageId = starboardMsg.Id;
            entry.IsPosted = true;
            await SaveStarredMessageEntryAsync(entry).ConfigureAwait(false);
            _logger.LogInformation(
                "[CreateStarboardPost] Posted message {OriginalMessageId} to starboard in guild {GuildId}",
                entry.Id.OriginalMessageId, guildId);
        }
        catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
        {
            _logger.LogError(ex,
                "[CreateStarboardPost] Forbidden to send message in starboard channel {ChannelId} for guild {GuildId}.",
                starboardChannel.Id, guildId);
        }
        catch (HttpException ex)
        {
            _logger.LogError(ex,
                "[CreateStarboardPost] Failed to send starboard message for {OriginalMessageId} in guild {GuildId}.",
                entry.Id.OriginalMessageId, guildId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[CreateStarboardPost] Unexpected error sending starboard message for {OriginalMessageId} in guild {GuildId}.",
                entry.Id.OriginalMessageId, guildId);
        }
    }

    private async Task UpdateStarboardPostAsync(StarredMessageModel entry, StarboardConfigModel config)
    {
        var guildId = entry.Id.GuildId;
        if (entry.StarboardMessageId == null || config.StarboardChannelId == null) return;

        var starboardChannel = _client.GetChannel(config.StarboardChannelId.Value) as ITextChannel ??
                               await _client.Rest.GetChannelAsync(config.StarboardChannelId.Value).ConfigureAwait(false) as ITextChannel;
        if (starboardChannel == null)
        {
            _logger.LogWarning(
                "[UpdateStarboardPost] Starboard channel {ChannelId} invalid for update in guild {GuildId}.",
                config.StarboardChannelId.Value, guildId);
            return;
        }

        try
        {
            if (await starboardChannel.GetMessageAsync(entry.StarboardMessageId.Value).ConfigureAwait(false) is not IUserMessage starboardMsg)
            {
                _logger.LogWarning(
                    "[UpdateStarboardPost] Starboard message {StarboardMessageId} not found or not IUserMessage in guild {GuildId}.",
                    entry.StarboardMessageId.Value, guildId);
                entry.IsPosted = false;
                entry.StarboardMessageId = null;
                await SaveStarredMessageEntryAsync(entry).ConfigureAwait(false);
                return;
            }

            var newContent = StarContentFormat.Replace("{emoji}", config.StarEmoji)
                .Replace("{count}", entry.StarCount.ToString())
                .Replace("{channelMention}", $"<#{entry.OriginalChannelId}>");

            if (starboardMsg.Content != newContent)
            {
                var guild = _client.GetGuild(guildId);
                if (guild == null)
                {
                    _logger.LogError("[UpdateStarboardPost] Could not get guild {GuildId} from cache.", guildId);
                    return;
                }

                var botUser = guild.CurrentUser;
                if (botUser == null)
                {
                    _logger.LogError(
                        "[UpdateStarboardPost] Could not get bot user (guild.CurrentUser) in guild {GuildId}.",
                        guildId);
                    return;
                }

                var perms = botUser.GetPermissions(starboardChannel);
                if (!perms.SendMessages)
                {
                    _logger.LogWarning(
                        "[UpdateStarboardPost] Missing Send permission to edit starboard message {MessageId} in channel {ChannelId}.",
                        entry.StarboardMessageId.Value, starboardChannel.Id);
                    return;
                }

                await starboardMsg.ModifyAsync(props => props.Content = newContent).ConfigureAwait(false);
                _logger.LogDebug(
                    "[UpdateStarboardPost] Updated star count for starboard message {StarboardMessageId} in guild {GuildId} to {StarCount}",
                    entry.StarboardMessageId.Value, guildId, entry.StarCount);
            }
        }
        catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning(
                "[UpdateStarboardPost] Starboard message {StarboardMessageId} not found via REST in guild {GuildId}. Unmarking as posted.",
                entry.StarboardMessageId, guildId);
            entry.IsPosted = false;
            entry.StarboardMessageId = null;
            await SaveStarredMessageEntryAsync(entry).ConfigureAwait(false);
        }
        catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
        {
            _logger.LogError(ex,
                "[UpdateStarboardPost] Forbidden to edit starboard message {StarboardMessageId} in guild {GuildId}.",
                entry.StarboardMessageId, guildId);
        }
        catch (HttpException ex)
        {
            _logger.LogError(ex,
                "[UpdateStarboardPost] Failed to edit starboard message {StarboardMessageId} in guild {GuildId}.",
                entry.StarboardMessageId, guildId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[UpdateStarboardPost] Unexpected error editing starboard message {StarboardMessageId} in guild {GuildId}.",
                entry.StarboardMessageId, guildId);
        }
    }

    private async Task DeleteStarboardPostAsync(StarredMessageModel entry, StarboardConfigModel config)
    {
        var guildId = entry.Id.GuildId;
        if (entry.StarboardMessageId == null || config.StarboardChannelId == null) return;

        var starboardChannel = _client.GetChannel(config.StarboardChannelId.Value) as ITextChannel ??
                               await _client.Rest.GetChannelAsync(config.StarboardChannelId.Value).ConfigureAwait(false) as ITextChannel;
        if (starboardChannel == null)
        {
            _logger.LogWarning(
                "[DeleteStarboardPost] Starboard channel {ChannelId} invalid for delete in guild {GuildId}.",
                config.StarboardChannelId.Value, guildId);
            entry.IsPosted = false;
            entry.StarboardMessageId = null;
            await SaveStarredMessageEntryAsync(entry).ConfigureAwait(false);
            return;
        }

        try
        {
            var guild = _client.GetGuild(guildId);
            if (guild == null)
            {
                _logger.LogError("[DeleteStarboardPost] Could not get guild {GuildId} from cache.", guildId);
                return;
            }

            var botUser = guild.CurrentUser;
            if (botUser == null)
            {
                _logger.LogError("[DeleteStarboardPost] Could not get bot user (guild.CurrentUser) in guild {GuildId}.",
                    guildId);
                return;
            }

            var perms = botUser.GetPermissions(starboardChannel);
            if (!perms.ManageMessages)
            {
                _logger.LogWarning(
                    "[DeleteStarboardPost] Missing Manage Messages permission to delete starboard message {MessageId} in channel {ChannelId}.",
                    entry.StarboardMessageId.Value, starboardChannel.Id);
                entry.IsPosted = false;
                entry.StarboardMessageId = null;
                await SaveStarredMessageEntryAsync(entry).ConfigureAwait(false);
                return;
            }

            await starboardChannel.DeleteMessageAsync(entry.StarboardMessageId.Value).ConfigureAwait(false);
            _logger.LogInformation(
                "[DeleteStarboardPost] Deleted starboard message {StarboardMessageId} for original {OriginalMessageId} in guild {GuildId}",
                entry.StarboardMessageId.Value, entry.Id.OriginalMessageId, guildId);
        }
        catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning(
                "[DeleteStarboardPost] Starboard message {StarboardMessageId} already deleted in guild {GuildId}.",
                entry.StarboardMessageId, guildId);
        }
        catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
        {
            _logger.LogError(ex,
                "[DeleteStarboardPost] Forbidden to delete starboard message {StarboardMessageId} in guild {GuildId}.",
                entry.StarboardMessageId, guildId);
        }
        catch (HttpException ex)
        {
            _logger.LogError(ex,
                "[DeleteStarboardPost] Failed to delete starboard message {StarboardMessageId} in guild {GuildId}.",
                entry.StarboardMessageId, guildId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[DeleteStarboardPost] Unexpected error deleting starboard message {StarboardMessageId} in guild {GuildId}.",
                entry.StarboardMessageId, guildId);
        }
        finally
        {
            if (entry.IsPosted || entry.StarboardMessageId != null)
            {
                entry.IsPosted = false;
                entry.StarboardMessageId = null;
                await SaveStarredMessageEntryAsync(entry).ConfigureAwait(false);
            }
        }
    }


    // --- Actual Event Processing Logic ---

    private async Task ProcessReactionAddedAsync(Cacheable<IUserMessage, ulong> messageCache,
        Cacheable<IMessageChannel, ulong> channelCache, SocketReaction reaction)
    {
        // --- Initial Checks ---
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
        if (!config.IsEnabled || config.StarboardChannelId == null) return;

        var emojiStr = reaction.Emote.ToString();
        if (emojiStr != config.StarEmoji) return;

        if (guildChannel.Id == config.StarboardChannelId.Value) return;

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

        // --- Process Star ---
        var entry = await GetStarredMessageEntryAsync(guildId, originalMessage.Id).ConfigureAwait(false);
        entry ??= new StarredMessageModel
        {
            Id = new StarredMessageIdKey { GuildId = guildId, OriginalMessageId = originalMessage.Id },
            OriginalChannelId = guildChannel.Id,
            StarrerUserIds = new HashSet<ulong>()
        };

        var starrerAdded = entry.StarrerUserIds.Add(reaction.UserId);
        if (starrerAdded)
        {
            entry.StarCount = entry.StarrerUserIds.Count;
            await SaveStarredMessageEntryAsync(entry).ConfigureAwait(false);

            if (entry.IsPosted) await UpdateStarboardPostAsync(entry, config).ConfigureAwait(false);
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
        // --- Initial Checks ---
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
        if (config.StarboardChannelId == null) return;

        var emojiStr = reaction.Emote.ToString();
        if (emojiStr != config.StarEmoji) return;
        if (guildChannel.Id == config.StarboardChannelId.Value) return;

        // --- Process Unstar ---
        var messageId = messageCache.Id;
        var entry = await GetStarredMessageEntryAsync(guildId, messageId).ConfigureAwait(false);
        if (entry == null) return;

        var starrerRemoved = entry.StarrerUserIds.Remove(reaction.UserId);
        if (starrerRemoved)
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
                    await UpdateStarboardPostAsync(entry, config).ConfigureAwait(false);
                    await SaveStarredMessageEntryAsync(entry).ConfigureAwait(false);
                }
            }
            else
            {
                await SaveStarredMessageEntryAsync(entry).ConfigureAwait(false);
            }
        }
        else
        {
            _logger.LogDebug("[ReactionRemoved] User {UserId} hadn't starred {MessageId}.", reaction.UserId, messageId);
        }
    }

    private async Task ProcessReactionsClearedAsync(Cacheable<IUserMessage, ulong> messageCache,
        Cacheable<IMessageChannel, ulong> channelCache)
    {
        // --- Initial Checks ---
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

        // --- Process Clear ---
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
                await UpdateStarboardPostAsync(entry, config).ConfigureAwait(false);
                await SaveStarredMessageEntryAsync(entry).ConfigureAwait(false);
            }
        }
        else
        {
            await SaveStarredMessageEntryAsync(entry).ConfigureAwait(false);
        }

        _logger.LogInformation("[ReactionsCleared] Cleared stars on message {MessageId} in guild {GuildId}", messageId,
            guildId);
    }

    private async Task ProcessMessageDeletedAsync(Cacheable<IMessage, ulong> messageCache,
        Cacheable<IMessageChannel, ulong> channelCache)
    {
        // --- Initial Checks ---
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
        if (config.StarboardChannelId == null) return;

        // --- Process Deletion ---
        var messageId = messageCache.Id;
        var entry = await GetStarredMessageEntryAsync(guildId, messageId).ConfigureAwait(false);

        if (entry is { IsPosted: true }) await DeleteStarboardPostAsync(entry, config).ConfigureAwait(false);

        // Delete DB entry regardless
        if (entry != null)
        {
            await DeleteStarredMessageEntryAsync(guildId, messageId).ConfigureAwait(false);
            _logger.LogDebug("[MessageDeleted] Deleted DB entry for {MessageId} in {GuildId}", messageId, guildId);
        }
    }

    private async Task ProcessMessagesBulkDeletedAsync(IReadOnlyCollection<Cacheable<IMessage, ulong>> messageCaches,
        Cacheable<IMessageChannel, ulong> channelCache)
    {
        // --- Initial Checks ---
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
        if (config.StarboardChannelId == null) return;

        var messageIds = messageCaches.Select(mc => mc.Id).ToList();
        if (messageIds.Count == 0) return;

        // --- Find Affected Starboard Entries ---
        var compositeIds = messageIds.Select(msgId => new StarredMessageIdKey
            { GuildId = guildId, OriginalMessageId = msgId });
        var filter = Builders<StarredMessageModel>.Filter.In(m => m.Id, compositeIds);
        filter &= Builders<StarredMessageModel>.Filter.Eq(m => m.IsPosted, true);

        var entriesToDeleteSbMsg = await _starredMessagesCollection.Find(filter).ToListAsync().ConfigureAwait(false);

        // --- Delete Starboard Posts ---
        if (entriesToDeleteSbMsg.Count != 0)
        {
            var deleteTasks = entriesToDeleteSbMsg.Select(entry => DeleteStarboardPostAsync(entry, config)).ToList();
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

        // --- Delete Database Records ---
        await DeleteManyStarredMessageEntriesAsync(guildId, messageIds).ConfigureAwait(false);
        _logger.LogInformation("[BulkDelete] Cleaned DB for {Count} bulk deleted messages in {GuildId}",
            messageIds.Count, guildId);
    }
}