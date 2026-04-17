using System.Net;
using Assistant.Net.Data.Entities;
using Assistant.Net.Data.Repositories.Interfaces;
using Assistant.Net.Utilities;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Services.Features;

public class StarboardService(
    DiscordSocketClient client,
    IUnitOfWorkFactory uowFactory,
    StarboardConfigService configService,
    ILogger<StarboardService> logger)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        client.ReactionAdded += HandleReactionAddedAsync;
        client.ReactionRemoved += HandleReactionRemovedAsync;
        client.ReactionsCleared += HandleReactionsClearedAsync;
        client.MessageDeleted += HandleMessageDeletedAsync;
        client.MessagesBulkDeleted += HandleMessagesBulkDeletedAsync;

        logger.LogInformation("StarboardService started and events hooked.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        client.ReactionAdded -= HandleReactionAddedAsync;
        client.ReactionRemoved -= HandleReactionRemovedAsync;
        client.ReactionsCleared -= HandleReactionsClearedAsync;
        client.MessageDeleted -= HandleMessageDeletedAsync;
        client.MessagesBulkDeleted -= HandleMessagesBulkDeletedAsync;

        logger.LogInformation("StarboardService stopped and events unhooked.");
        return Task.CompletedTask;
    }

    private Task HandleReactionAddedAsync(Cacheable<IUserMessage, ulong> msg, Cacheable<IMessageChannel, ulong> chan,
        SocketReaction reaction)
    {
        return Task.Run(async () =>
        {
            try
            {
                await ProcessReactionAddedAsync(msg, chan, reaction).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing ReactionAdded event.");
            }
        });
    }

    private Task HandleReactionRemovedAsync(Cacheable<IUserMessage, ulong> msg, Cacheable<IMessageChannel, ulong> chan,
        SocketReaction reaction)
    {
        return Task.Run(async () =>
        {
            try
            {
                await ProcessReactionRemovedAsync(msg, chan, reaction).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing ReactionRemoved event.");
            }
        });
    }

    private Task HandleReactionsClearedAsync(Cacheable<IUserMessage, ulong> msg, Cacheable<IMessageChannel, ulong> chan)
    {
        return Task.Run(async () =>
        {
            try
            {
                await ProcessReactionsClearedAsync(msg, chan).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing ReactionsCleared event.");
            }
        });
    }

    private Task HandleMessageDeletedAsync(Cacheable<IMessage, ulong> msg, Cacheable<IMessageChannel, ulong> chan)
    {
        return Task.Run(async () =>
        {
            try
            {
                await ProcessMessageDeletedAsync(msg, chan).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing MessageDeleted event.");
            }
        });
    }

    private Task HandleMessagesBulkDeletedAsync(IReadOnlyCollection<Cacheable<IMessage, ulong>> msgs,
        Cacheable<IMessageChannel, ulong> chan)
    {
        return Task.Run(async () =>
        {
            try
            {
                await ProcessMessagesBulkDeletedAsync(msgs, chan).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing MessagesBulkDeleted event.");
            }
        });
    }

    private async Task ProcessReactionAddedAsync(Cacheable<IUserMessage, ulong> messageCache,
        Cacheable<IMessageChannel, ulong> channelCache, SocketReaction reaction)
    {
        var channel = channelCache.HasValue
            ? channelCache.Value
            : await channelCache.GetOrDownloadAsync().ConfigureAwait(false);
        if (channel is not SocketGuildChannel guildChannel) return;
        var guildId = guildChannel.Guild.Id;

        if (reaction.UserId == client.CurrentUser.Id) return;

        var config = await configService.GetGuildConfigAsync(guildId).ConfigureAwait(false);
        if (!config.IsEnabled || config.StarboardChannelId == null || reaction.Emote.ToString() != config.StarEmoji ||
            guildChannel.Id == config.StarboardChannelId.Value) return;

        var originalMessage = await messageCache.GetOrDownloadAsync().ConfigureAwait(false);
        if (originalMessage == null) return;

        if (!config.AllowBotMessages && originalMessage.Author.IsBot) return;
        if (!config.AllowSelfStar && reaction.UserId == originalMessage.Author.Id)
        {
            var botUser = guildChannel.Guild.CurrentUser;
            if (botUser.GetPermissions(guildChannel).ManageMessages)
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

        await using var uow = await uowFactory.CreateAsync().ConfigureAwait(false);
        var entry = await uow.Starboard.GetStarredMessageAsync(guildId, originalMessage.Id).ConfigureAwait(false);

        if (entry == null)
        {
            await uow.Users.EnsureExistsAsync(originalMessage.Author.Id).ConfigureAwait(false);
            await uow.Guilds.EnsureExistsAsync(guildId).ConfigureAwait(false);
            entry = new StarredMessageEntity
            {
                GuildId = guildId,
                OriginalMessageId = originalMessage.Id,
                OriginalChannelId = guildChannel.Id,
                AuthorId = originalMessage.Author.Id,
                StarCount = 0,
                IsPosted = false
            };
            uow.Starboard.AddStarredMessage(entry);
            await uow.SaveChangesAsync().ConfigureAwait(false);
        }

        if (entry.Votes.All(v => v.UserId != reaction.UserId))
        {
            await uow.Users.EnsureExistsAsync(reaction.UserId).ConfigureAwait(false);

            uow.Starboard.AddVote(new StarVoteEntity
            {
                StarredMessageId = entry.Id,
                UserId = reaction.UserId
            });
            entry.StarCount++;
            entry.LastUpdated = DateTime.UtcNow;

            await uow.SaveChangesAsync().ConfigureAwait(false);

            if (entry.IsPosted)
                await UpdateStarboardPostAsync(uow, entry, config, originalMessage).ConfigureAwait(false);
            else if (entry.StarCount >= config.Threshold)
                await CreateStarboardPostAsync(uow, originalMessage, entry, config).ConfigureAwait(false);
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

        var config = await configService.GetGuildConfigAsync(guildId).ConfigureAwait(false);
        if (config.StarboardChannelId == null || reaction.Emote.ToString() != config.StarEmoji ||
            guildChannel.Id == config.StarboardChannelId.Value) return;

        await using var uow = await uowFactory.CreateAsync().ConfigureAwait(false);
        var entry = await uow.Starboard.GetStarredMessageAsync(guildId, messageCache.Id).ConfigureAwait(false);
        if (entry == null) return;

        var voteToRemove = entry.Votes.FirstOrDefault(v => v.UserId == reaction.UserId);
        if (voteToRemove != null)
        {
            uow.Starboard.RemoveVote(voteToRemove);
            entry.StarCount = Math.Max(0, entry.StarCount - 1);
            entry.LastUpdated = DateTime.UtcNow;
            await uow.SaveChangesAsync().ConfigureAwait(false);

            if (entry.IsPosted)
            {
                if (entry.StarCount < config.Threshold && config.DeleteIfUnStarred)
                {
                    await DeleteStarboardPostAsync(uow, entry, config).ConfigureAwait(false);
                }
                else
                {
                    var originalMessage = await messageCache.GetOrDownloadAsync().ConfigureAwait(false);
                    await UpdateStarboardPostAsync(uow, entry, config, originalMessage).ConfigureAwait(false);
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

        var config = await configService.GetGuildConfigAsync(guildId).ConfigureAwait(false);
        if (config.StarboardChannelId == null) return;

        await using var uow = await uowFactory.CreateAsync().ConfigureAwait(false);
        var entry = await uow.Starboard.GetStarredMessageAsync(guildId, messageCache.Id).ConfigureAwait(false);
        if (entry == null) return;

        uow.Starboard.RemoveVotes(entry.Votes);
        entry.StarCount = 0;
        entry.LastUpdated = DateTime.UtcNow;
        await uow.SaveChangesAsync().ConfigureAwait(false);

        if (entry.IsPosted)
        {
            if (config.DeleteIfUnStarred)
            {
                await DeleteStarboardPostAsync(uow, entry, config).ConfigureAwait(false);
            }
            else
            {
                var originalMessage = await messageCache.GetOrDownloadAsync().ConfigureAwait(false);
                await UpdateStarboardPostAsync(uow, entry, config, originalMessage).ConfigureAwait(false);
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

        var config = await configService.GetGuildConfigAsync(guildId).ConfigureAwait(false);

        await using var uow = await uowFactory.CreateAsync().ConfigureAwait(false);
        var entry = await uow.Starboard.GetStarredMessageAsync(guildId, messageCache.Id).ConfigureAwait(false);

        if (entry == null) return;

        if (entry.IsPosted && config.StarboardChannelId.HasValue)
            await DeleteStarboardPostAsync(uow, entry, config).ConfigureAwait(false);

        uow.Starboard.RemoveStarredMessage(entry);
        await uow.SaveChangesAsync().ConfigureAwait(false);
    }

    private async Task ProcessMessagesBulkDeletedAsync(IReadOnlyCollection<Cacheable<IMessage, ulong>> messageCaches,
        Cacheable<IMessageChannel, ulong> channelCache)
    {
        var channel = channelCache.HasValue
            ? channelCache.Value
            : await channelCache.GetOrDownloadAsync().ConfigureAwait(false);
        if (channel is not SocketGuildChannel guildChannel) return;
        var guildId = guildChannel.Guild.Id;

        var config = await configService.GetGuildConfigAsync(guildId).ConfigureAwait(false);
        var messageIds = messageCaches.Select(m => m.Id).ToList();

        await using var uow = await uowFactory.CreateAsync().ConfigureAwait(false);
        var entries = await uow.Starboard.GetStarredMessagesByOriginalIdsAsync(guildId, messageIds)
            .ConfigureAwait(false);

        if (entries.Count == 0) return;

        foreach (var entry in entries)
            if (entry.IsPosted && config.StarboardChannelId.HasValue)
                await DeleteStarboardPostAsync(uow, entry, config).ConfigureAwait(false);

        uow.Starboard.RemoveStarredMessages(entries);
        await uow.SaveChangesAsync().ConfigureAwait(false);
    }

    private async Task CreateStarboardPostAsync(IUnitOfWork uow, IMessage originalMessage, StarredMessageEntity entry,
        StarboardConfigEntity config)
    {
        var components = BuildStarboardComponents(originalMessage, entry.StarCount, config.StarEmoji);

        var (success, sentMessage) =
            await ExecuteStarboardActionAsync(config, entry, CreateAction, null!, null!, "CreateStarboardPost", true)
                .ConfigureAwait(false);

        if (!success || sentMessage == null) return;

        entry.StarboardMessageId = sentMessage.Id;
        entry.IsPosted = true;
        entry.LastUpdated = DateTime.UtcNow;
        await uow.SaveChangesAsync().ConfigureAwait(false);

        return;

        async Task<IUserMessage?> CreateAction(ITextChannel channel) =>
            await channel.SendMessageAsync(components: components, allowedMentions: AllowedMentions.None,
                flags: MessageFlags.ComponentsV2).ConfigureAwait(false);
    }

    private async Task UpdateStarboardPostAsync(IUnitOfWork uow, StarredMessageEntity entry,
        StarboardConfigEntity config, IMessage? originalMessage = null)
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
                if (client.GetChannel(currentEntry.OriginalChannelId) is ITextChannel originalChannel)
                    resolvedOriginalMessage = await originalChannel
                        .GetMessageAsync(currentEntry.OriginalMessageId).ConfigureAwait(false);
                else return;
            }

            if (resolvedOriginalMessage == null) return;

            if (await channel.GetMessageAsync(currentEntry.StarboardMessageId!.Value).ConfigureAwait(false) is
                not IUserMessage starboardMsg)
            {
                currentEntry.IsPosted = false;
                currentEntry.StarboardMessageId = null;
                currentEntry.LastUpdated = DateTime.UtcNow;
                await uow.SaveChangesAsync().ConfigureAwait(false);
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

    private async Task DeleteStarboardPostAsync(IUnitOfWork uow, StarredMessageEntity entry,
        StarboardConfigEntity config)
    {
        if (entry.StarboardMessageId == null)
        {
            if (!entry.IsPosted) return;
            entry.IsPosted = false;
            await uow.SaveChangesAsync().ConfigureAwait(false);
            return;
        }

        await ExecuteStarboardActionAsync(config, entry, null!, null!, DeleteAction, "DeleteStarboardPost",
            isDelete: true).ConfigureAwait(false);

        entry.IsPosted = false;
        entry.StarboardMessageId = null;
        await uow.SaveChangesAsync().ConfigureAwait(false);

        return;

        async Task DeleteAction(StarredMessageEntity currentEntry, ITextChannel channel) =>
            await channel.DeleteMessageAsync(currentEntry.StarboardMessageId!.Value).ConfigureAwait(false);
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
            else otherAttachments.Add(attachment);

        if (imageUrls.Count > 0) container.WithMediaGallery(imageUrls);
        if (otherAttachments.Count > 0)
        {
            var attachmentsText = string.Join("\n",
                otherAttachments.Select(a => $"📄 [{a.Filename}]({a.Url}) ({a.Size.ToHumanSize()})"));
            container.WithTextDisplay(new TextDisplayBuilder($"**Attachments:**\n{attachmentsText}"));
        }

        container.WithSeparator();
        var starText = $"{starEmoji} **{starCount}** in {MentionUtils.MentionChannel(originalMessage.Channel.Id)}";
        var timeText = $"{originalMessage.CreatedAt.GetRelativeTime()}";
        container.WithTextDisplay(new TextDisplayBuilder($"{starText}  •  {timeText}"));
        container.WithActionRow(row =>
            row.WithButton("Jump to Message", style: ButtonStyle.Link, url: originalMessage.GetJumpUrl()));

        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    private async Task<(bool Success, IUserMessage? Message)> ExecuteStarboardActionAsync(StarboardConfigEntity config,
        StarredMessageEntity? entry, Func<ITextChannel, Task<IUserMessage?>> action,
        Func<StarredMessageEntity, ITextChannel, Task> updateAction,
        Func<StarredMessageEntity, ITextChannel, Task> deleteAction, string logContext, bool isCreate = false,
        bool isUpdate = false, bool isDelete = false)
    {
        if (config.StarboardChannelId == null)
        {
            logger.LogWarning("[{Context}] Starboard channel ID is null for guild {GuildId}.", logContext,
                config.GuildId);
            return (false, null);
        }

        var channelId = config.StarboardChannelId.Value;
        var starboardChannel = client.GetChannel(channelId) as ITextChannel ??
                               await client.Rest.GetChannelAsync(channelId).ConfigureAwait(false) as ITextChannel;

        if (starboardChannel == null)
        {
            logger.LogWarning("[{Context}] Starboard channel {ChannelId} not found.", logContext, channelId);
            return (false, null);
        }

        var guild = client.GetGuild(config.GuildId);
        if (guild == null)
        {
            logger.LogError("[{Context}] Could not get guild {GuildId} from cache.", logContext, config.GuildId);
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
            logger.LogError(ex, "[{Context}] Error performing action in starboard channel.", logContext);
            return (false, null);
        }
    }
}