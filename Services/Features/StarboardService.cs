using System.Net;
using Assistant.Net.Data;
using Assistant.Net.Data.Entities;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Services.Features;

public class StarboardService
{
    private readonly DiscordSocketClient _client;
    private readonly StarboardConfigService _configService;
    private readonly IDbContextFactory<AssistantDbContext> _dbFactory;
    private readonly ILogger<StarboardService> _logger;

    public StarboardService(
        DiscordSocketClient client,
        IDbContextFactory<AssistantDbContext> dbFactory,
        StarboardConfigService configService,
        ILogger<StarboardService> logger)
    {
        _client = client;
        _dbFactory = dbFactory;
        _configService = configService;
        _logger = logger;

        _client.ReactionAdded += HandleReactionAddedAsync;
        _client.ReactionRemoved += HandleReactionRemovedAsync;
        _client.ReactionsCleared += HandleReactionsClearedAsync;
        _client.MessageDeleted += HandleMessageDeletedAsync;
        _client.MessagesBulkDeleted += HandleMessagesBulkDeletedAsync;

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


    private static async Task<StarredMessageEntity?> GetStarredMessageEntryAsync(AssistantDbContext context,
        ulong guildId,
        ulong originalMessageId)
    {
        return await context.StarredMessages
            .Include(sm => sm.Votes)
            .FirstOrDefaultAsync(sm =>
                sm.GuildId == guildId && sm.OriginalMessageId == originalMessageId)
            .ConfigureAwait(false);
    }

    private static async Task SaveStarredMessageEntryAsync(AssistantDbContext context, StarredMessageEntity entry)
    {
        entry.LastUpdated = DateTime.UtcNow;
        if (entry.Id == 0)
            context.StarredMessages.Add(entry);

        await context.SaveChangesAsync().ConfigureAwait(false);
    }


    private async Task ProcessReactionAddedAsync(Cacheable<IUserMessage, ulong> messageCache,
        Cacheable<IMessageChannel, ulong> channelCache, SocketReaction reaction)
    {
        var channel = channelCache.HasValue
            ? channelCache.Value
            : await channelCache.GetOrDownloadAsync().ConfigureAwait(false);

        if (channel is not SocketGuildChannel guildChannel) return;
        var guildId = guildChannel.Guild.Id;

        if (reaction.UserId == _client.CurrentUser.Id) return;

        var config = await _configService.GetGuildConfigAsync(guildId).ConfigureAwait(false);
        if (!config.IsEnabled || config.StarboardChannelId == null || reaction.Emote.ToString() != config.StarEmoji ||
            guildChannel.Id == (ulong)config.StarboardChannelId.Value) return;

        var originalMessage = await messageCache.GetOrDownloadAsync().ConfigureAwait(false);
        if (originalMessage == null) return;

        if (!config.AllowBotMessages && originalMessage.Author.IsBot) return;
        if (!config.AllowSelfStar && reaction.UserId == originalMessage.Author.Id)
        {
            var botUser = guildChannel.Guild.CurrentUser;
            if (!botUser.GetPermissions(guildChannel).ManageMessages) return;
            try
            {
                await originalMessage.RemoveReactionAsync(reaction.Emote, reaction.UserId).ConfigureAwait(false);
            }
            catch
            {
                /* ignored */
            }

            return;
        }

        if (config.IgnoreNsfwChannels && guildChannel is ITextChannel { IsNsfw: true }) return;

        await using var context = await _dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        var entry = await GetStarredMessageEntryAsync(context, guildId, originalMessage.Id).ConfigureAwait(false);

        if (entry == null)
        {
            // Ensure Author Exists
            if (!await context.Users.AnyAsync(u => u.Id == originalMessage.Author.Id))
                context.Users.Add(new UserEntity { Id = originalMessage.Author.Id });

            entry = new StarredMessageEntity
            {
                GuildId = guildId,
                OriginalMessageId = originalMessage.Id,
                OriginalChannelId = guildChannel.Id,
                AuthorId = originalMessage.Author.Id,
                StarCount = 0,
                IsPosted = false
            };
            context.StarredMessages.Add(entry);
            await context.SaveChangesAsync().ConfigureAwait(false);
        }

        // Add Vote
        if (entry.Votes.All(v => v.UserId != reaction.UserId))
        {
            if (!await context.Users.AnyAsync(u => u.Id == reaction.UserId))
            {
                context.Users.Add(new UserEntity { Id = reaction.UserId });
                await context.SaveChangesAsync().ConfigureAwait(false);
            }

            entry.Votes.Add(new StarVoteEntity
            {
                StarredMessageId = entry.Id,
                UserId = reaction.UserId
            });
            entry.StarCount++;
            await SaveStarredMessageEntryAsync(context, entry).ConfigureAwait(false);

            if (entry.IsPosted)
                await UpdateStarboardPostAsync(entry, config, originalMessage).ConfigureAwait(false);
            else if (entry.StarCount >= config.Threshold)
                await CreateStarboardPostAsync(originalMessage, entry, config).ConfigureAwait(false);
        }
    }

    private async Task ProcessReactionRemovedAsync(Cacheable<IUserMessage, ulong> messageCache,
        Cacheable<IMessageChannel, ulong> channelCache, SocketReaction reaction)
    {
        var channel = channelCache.HasValue
            ? channelCache.Value
            : await channelCache.GetOrDownloadAsync().ConfigureAwait(false);

        if (channel is not SocketGuildChannel guildChannel) return;
        var guildId = guildChannel.Guild.Id;

        var config = await _configService.GetGuildConfigAsync(guildId).ConfigureAwait(false);
        if (config.StarboardChannelId == null || reaction.Emote.ToString() != config.StarEmoji ||
            guildChannel.Id == (ulong)config.StarboardChannelId.Value) return;

        await using var context = await _dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        var entry = await GetStarredMessageEntryAsync(context, guildId, messageCache.Id).ConfigureAwait(false);
        if (entry == null) return;

        var voteToRemove = entry.Votes.FirstOrDefault(v => v.UserId == reaction.UserId);
        if (voteToRemove != null)
        {
            context.StarVotes.Remove(voteToRemove);
            entry.StarCount = Math.Max(0, entry.StarCount - 1);
            await SaveStarredMessageEntryAsync(context, entry).ConfigureAwait(false);

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
        }
    }

    private async Task ProcessReactionsClearedAsync(Cacheable<IUserMessage, ulong> messageCache,
        Cacheable<IMessageChannel, ulong> channelCache)
    {
        var channel = channelCache.HasValue
            ? channelCache.Value
            : await channelCache.GetOrDownloadAsync().ConfigureAwait(false);

        if (channel is not SocketGuildChannel guildChannel) return;
        var guildId = guildChannel.Guild.Id;

        var config = await _configService.GetGuildConfigAsync(guildId).ConfigureAwait(false);
        if (config.StarboardChannelId == null) return;

        await using var context = await _dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        var entry = await GetStarredMessageEntryAsync(context, guildId, messageCache.Id).ConfigureAwait(false);
        if (entry == null) return;

        context.StarVotes.RemoveRange(entry.Votes);
        entry.StarCount = 0;
        await SaveStarredMessageEntryAsync(context, entry).ConfigureAwait(false);

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
    }

    private async Task ProcessMessageDeletedAsync(Cacheable<IMessage, ulong> messageCache,
        Cacheable<IMessageChannel, ulong> channelCache)
    {
        var channel = channelCache.HasValue
            ? channelCache.Value
            : await channelCache.GetOrDownloadAsync().ConfigureAwait(false);

        if (channel is not SocketGuildChannel guildChannel) return;
        var guildId = guildChannel.Guild.Id;

        var config = await _configService.GetGuildConfigAsync(guildId).ConfigureAwait(false);

        await using var context = await _dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        var entry = await GetStarredMessageEntryAsync(context, guildId, messageCache.Id).ConfigureAwait(false);

        if (entry == null) return;

        if (entry.IsPosted && config.StarboardChannelId.HasValue)
            await DeleteStarboardPostAsync(entry, config).ConfigureAwait(false);

        context.StarredMessages.Remove(entry);
        await context.SaveChangesAsync().ConfigureAwait(false);
    }

    private async Task ProcessMessagesBulkDeletedAsync(IReadOnlyCollection<Cacheable<IMessage, ulong>> messageCaches,
        Cacheable<IMessageChannel, ulong> channelCache)
    {
        var channel = channelCache.HasValue
            ? channelCache.Value
            : await channelCache.GetOrDownloadAsync().ConfigureAwait(false);

        if (channel is not SocketGuildChannel guildChannel) return;
        var guildId = guildChannel.Guild.Id;

        var config = await _configService.GetGuildConfigAsync(guildId).ConfigureAwait(false);
        var messageIds = messageCaches.Select(m => (decimal)m.Id).ToList();

        await using var context = await _dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        var entries = await context.StarredMessages
            .Where(sm => sm.GuildId == guildId && messageIds.Contains(sm.OriginalMessageId))
            .ToListAsync().ConfigureAwait(false);

        if (entries.Count == 0) return;

        foreach (var entry in entries)
            if (entry.IsPosted && config.StarboardChannelId.HasValue)
                await DeleteStarboardPostAsync(entry, config).ConfigureAwait(false);

        context.StarredMessages.RemoveRange(entries);
        await context.SaveChangesAsync().ConfigureAwait(false);
    }


    private async Task CreateStarboardPostAsync(IMessage originalMessage, StarredMessageEntity entry,
        StarboardConfigEntity config)
    {
        var components = BuildStarboardComponents(originalMessage, entry.StarCount, config.StarEmoji);

        var (success, sentMessage) =
            await ExecuteStarboardActionAsync(config, entry, CreateAction, null!, null!, "CreateStarboardPost", true)
                .ConfigureAwait(false);

        if (!success || sentMessage == null) return;

        await using var context = await _dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        var dbEntry = await context.StarredMessages.FindAsync(entry.Id);

        if (dbEntry == null) return;

        dbEntry.StarboardMessageId = sentMessage.Id;
        dbEntry.IsPosted = true;
        dbEntry.LastUpdated = DateTime.UtcNow;
        await context.SaveChangesAsync().ConfigureAwait(false);

        return;

        async Task<IUserMessage?> CreateAction(ITextChannel channel) =>
            await channel.SendMessageAsync(components: components, allowedMentions: AllowedMentions.None,
                flags: MessageFlags.ComponentsV2).ConfigureAwait(false);
    }

    private async Task UpdateStarboardPostAsync(StarredMessageEntity entry, StarboardConfigEntity config,
        IMessage? originalMessage = null)
    {
        if (entry.StarboardMessageId == null) return;

        await ExecuteStarboardActionAsync(config, entry, null!, UpdateAction, null!, "UpdateStarboardPost",
            isUpdate: true).ConfigureAwait(false);

        return;

        async Task UpdateAction(StarredMessageEntity currentEntry, ITextChannel channel)
        {
            var resolvedOriginalMessage = originalMessage;
            if (resolvedOriginalMessage == null)
            {
                if (_client.GetChannel((ulong)currentEntry.OriginalChannelId) is ITextChannel originalChannel)
                    resolvedOriginalMessage = await originalChannel
                        .GetMessageAsync((ulong)currentEntry.OriginalMessageId)
                        .ConfigureAwait(false);
                else return;
            }

            if (resolvedOriginalMessage == null) return;

            if (await channel.GetMessageAsync((ulong)currentEntry.StarboardMessageId!.Value)
                    .ConfigureAwait(false) is not
                IUserMessage starboardMsg)
            {
                await using var context = await _dbFactory.CreateDbContextAsync().ConfigureAwait(false);
                var dbEntry = await context.StarredMessages.FindAsync(currentEntry.Id);

                if (dbEntry == null) throw new HttpException(HttpStatusCode.NotFound, null);

                dbEntry.IsPosted = false;
                dbEntry.StarboardMessageId = null;
                await context.SaveChangesAsync().ConfigureAwait(false);
                throw new HttpException(HttpStatusCode.NotFound, null);
            }

            var newComponents =
                BuildStarboardComponents(resolvedOriginalMessage, currentEntry.StarCount, config.StarEmoji);
            await starboardMsg.ModifyAsync(props =>
            {
                props.Content = "";
                props.Components = newComponents;
            }).ConfigureAwait(false);
        }
    }

    private async Task DeleteStarboardPostAsync(StarredMessageEntity entry, StarboardConfigEntity config)
    {
        if (entry.StarboardMessageId == null)
        {
            if (!entry.IsPosted) return;
            await using var context = await _dbFactory.CreateDbContextAsync().ConfigureAwait(false);
            var dbEntry = await context.StarredMessages.FindAsync(entry.Id);
            if (dbEntry == null) return;
            dbEntry.IsPosted = false;
            await context.SaveChangesAsync().ConfigureAwait(false);

            return;
        }

        await ExecuteStarboardActionAsync(config, entry, null!, null!, DeleteAction, "DeleteStarboardPost",
            isDelete: true).ConfigureAwait(false);

        if (entry is { IsPosted: false, StarboardMessageId: null }) return;
        {
            await using var context = await _dbFactory.CreateDbContextAsync().ConfigureAwait(false);
            var dbEntry = await context.StarredMessages.FindAsync(entry.Id);
            if (dbEntry == null) return;
            dbEntry.IsPosted = false;
            dbEntry.StarboardMessageId = null;
            await context.SaveChangesAsync().ConfigureAwait(false);
        }

        return;

        async Task DeleteAction(StarredMessageEntity currentEntry, ITextChannel channel) =>
            await channel.DeleteMessageAsync((ulong)currentEntry.StarboardMessageId!.Value).ConfigureAwait(false);
    }

    private static MessageComponent BuildStarboardComponents(IMessage originalMessage, int starCount, string starEmoji)
    {
        var container = new ContainerBuilder();

        container.WithSection(section =>
        {
            section.AddComponent(new TextDisplayBuilder($"## {originalMessage.Author.Mention}"));
            if (!string.IsNullOrWhiteSpace(originalMessage.Content))
                section.AddComponent(new TextDisplayBuilder(originalMessage.Content));
            section.WithAccessory(new ThumbnailBuilder
            {
                Media = new UnfurledMediaItemProperties
                {
                    Url = originalMessage.Author.GetDisplayAvatarUrl() ?? originalMessage.Author.GetDefaultAvatarUrl()
                }
            });
        });

        var imageUrls = new List<string>();
        var otherAttachments = new List<IAttachment>();

        foreach (var embed in originalMessage.Embeds)
            if (embed.Image.HasValue) imageUrls.Add(embed.Image.Value.Url);
            else if (embed.Thumbnail.HasValue) imageUrls.Add(embed.Thumbnail.Value.Url);

        foreach (var attachment in originalMessage.Attachments)
            if (attachment.ContentType?.StartsWith("image/") == true && attachment.Height.HasValue)
                imageUrls.Add(attachment.Url);
            else
                otherAttachments.Add(attachment);

        if (imageUrls.Count > 0) container.WithMediaGallery(imageUrls);

        if (otherAttachments.Count > 0)
        {
            var attachmentsText = string.Join("\n", otherAttachments.Select(a => $"📄 [{a.Filename}]({a.Url})"));
            container.WithTextDisplay(new TextDisplayBuilder($"**Attachments:**\n{attachmentsText}"));
        }

        container.WithSeparator();

        var starText = $"{starEmoji} **{starCount}** in {MentionUtils.MentionChannel(originalMessage.Channel.Id)}";
        var timeText =
            $"{TimestampTag.FormatFromDateTimeOffset(originalMessage.CreatedAt, TimestampTagStyles.Relative)}";
        container.WithTextDisplay(new TextDisplayBuilder($"{starText}  •  {timeText}"));

        container.WithActionRow(row =>
            row.WithButton("Jump to Message", style: ButtonStyle.Link, url: originalMessage.GetJumpUrl()));

        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    private async Task<(bool Success, IUserMessage? Message)> ExecuteStarboardActionAsync(
        StarboardConfigEntity config,
        StarredMessageEntity? entry,
        Func<ITextChannel, Task<IUserMessage?>> action,
        Func<StarredMessageEntity, ITextChannel, Task> updateAction,
        Func<StarredMessageEntity, ITextChannel, Task> deleteAction,
        string logContext,
        bool isCreate = false, bool isUpdate = false, bool isDelete = false)
    {
        if (config.StarboardChannelId == null)
        {
            _logger.LogWarning("[{Context}] Starboard channel ID is null for guild {GuildId}.", logContext,
                config.GuildId);
            return (false, null);
        }

        var channelId = (ulong)config.StarboardChannelId.Value;
        var starboardChannel = _client.GetChannel(channelId) as ITextChannel ??
                               await _client.Rest.GetChannelAsync(channelId).ConfigureAwait(false) as ITextChannel;

        if (starboardChannel == null)
        {
            _logger.LogWarning("[{Context}] Starboard channel {ChannelId} not found.", logContext, channelId);
            return (false, null);
        }

        var guild = _client.GetGuild((ulong)config.GuildId);
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Context}] Error performing action in starboard channel.", logContext);
            return (false, null);
        }
    }
}