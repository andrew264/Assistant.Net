using System.Collections.Concurrent;
using System.Net;
using Assistant.Net.Configuration;
using Assistant.Net.Utilities;
using Discord;
using Discord.Net;
using Discord.Webhook;
using Discord.WebSocket;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Services;

public class SurveillanceService
{
    private const string WebhookCachePrefix = "webhook:";
    private const string AssistantWebhookName = "Assistant";
    private static readonly TimeSpan WebhookCacheDuration = TimeSpan.FromHours(1);
    private readonly DiscordSocketClient _client;
    private readonly Config _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SurveillanceService> _logger;
    private readonly IMemoryCache _memoryCache;
    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _webhookLocks = new();

    public SurveillanceService(
        DiscordSocketClient client,
        Config config,
        ILogger<SurveillanceService> logger,
        IHttpClientFactory httpClientFactory,
        IMemoryCache memoryCache)
    {
        client.MessageUpdated += HandleMessageUpdatedAsync;
        client.MessageDeleted += HandleMessageDeletedAsync;
        client.GuildMemberUpdated += HandleGuildMemberUpdatedAsync;
        client.UserUpdated += HandleUserUpdatedAsync;
        client.PresenceUpdated += HandlePresenceUpdatedAsync;
        client.UserVoiceStateUpdated += HandleVoiceStateUpdatedAsync;
        client.UserIsTyping += HandleTypingAsync;
        client.UserJoined += HandleUserJoinedAsync;
        client.UserLeft += HandleUserLeftAsync;
        client.UserBanned += HandleUserBannedAsync;
        client.UserUnbanned += HandleUserUnbannedAsync;
        _client = client;
        _config = config;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _memoryCache = memoryCache;
        _logger.LogInformation("SurveillanceService initialized and events hooked.");
    }


    // --- Webhook Management ---

    private async Task<DiscordWebhookClient?> GetWebhookAsync(ulong channelId)
    {
        var cacheKey = $"{WebhookCachePrefix}{channelId}";

        if (_memoryCache.TryGetValue(cacheKey, out DiscordWebhookClient? cachedClient) && cachedClient != null)
            return cachedClient;

        var channelLock = _webhookLocks.GetOrAdd(channelId, _ => new SemaphoreSlim(1, 1));
        await channelLock.WaitAsync();

        IWebhook? existingWebhook = null;
        try
        {
            if (_memoryCache.TryGetValue(cacheKey, out cachedClient) && cachedClient != null) return cachedClient;

            var channel = _client.GetChannel(channelId);
            if (channel is not SocketTextChannel textChannel)
            {
                _logger.LogWarning("Logging channel {ChannelId} not found or is not a text channel.", channelId);
                return null;
            }

            var botUser = _client.CurrentUser;
            var guild = textChannel.Guild;
            var botGuildUser = guild.GetUser(botUser.Id);

            if (botGuildUser == null || !botGuildUser.GuildPermissions.ManageWebhooks)
            {
                _logger.LogError(
                    "Bot lacks 'Manage Webhooks' permission in logging channel {ChannelId} ({ChannelName}) in Guild {GuildId}.",
                    channelId, textChannel.Name, guild.Id);
                return null;
            }

            try
            {
                var webhooks = await textChannel.GetWebhooksAsync();
                existingWebhook = webhooks.FirstOrDefault(w => w.Name == AssistantWebhookName);
            }
            catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
            {
                _logger.LogError(ex, "Forbidden to get webhooks in channel {ChannelId} ({ChannelName}).", channelId,
                    textChannel.Name);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get webhooks for channel {ChannelId} ({ChannelName}).", channelId,
                    textChannel.Name);
            }


            if (existingWebhook != null)
            {
                _logger.LogDebug("Found existing webhook '{WebhookName}' ({WebhookId}) in channel {ChannelId}.",
                    existingWebhook.Name, existingWebhook.Id, channelId);
                var webhookClient = new DiscordWebhookClient(existingWebhook.Id, existingWebhook.Token);
                _memoryCache.Set(cacheKey, webhookClient, WebhookCacheDuration);
                return webhookClient;
            }
            else
            {
                _logger.LogInformation("Webhook '{WebhookName}' not found in channel {ChannelId}. Creating new one.",
                    AssistantWebhookName, channelId);
                try
                {
                    Stream? avatarStream = null;
                    var avatarUrl = botUser.GetAvatarUrl() ?? botUser.GetDefaultAvatarUrl();
                    try
                    {
                        var httpClient = _httpClientFactory.CreateClient();
                        var response = await httpClient.GetAsync(avatarUrl);
                        response.EnsureSuccessStatusCode();
                        avatarStream = await response.Content.ReadAsStreamAsync();
                    }
                    catch (Exception avatarEx)
                    {
                        _logger.LogWarning(avatarEx,
                            "Failed to download bot avatar for webhook creation. Using default.");
                        if (avatarStream != null)
                            await avatarStream.DisposeAsync();
                        avatarStream = null;
                    }


                    var newWebhook = await textChannel.CreateWebhookAsync(AssistantWebhookName, avatarStream);
                    if (avatarStream != null)
                        await avatarStream.DisposeAsync();

                    _logger.LogInformation("Created webhook '{WebhookName}' ({WebhookId}) in channel {ChannelId}.",
                        newWebhook.Name, newWebhook.Id, channelId);
                    var webhookClient = new DiscordWebhookClient(newWebhook.Id, newWebhook.Token);
                    _memoryCache.Set(cacheKey, webhookClient, WebhookCacheDuration);
                    return webhookClient;
                }
                catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
                {
                    _logger.LogError(ex, "Forbidden to create webhook in channel {ChannelId} ({ChannelName}).",
                        channelId, textChannel.Name);
                    return null;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create webhook in channel {ChannelId} ({ChannelName}).", channelId,
                        textChannel.Name);
                    return null;
                }
            }
        }
        finally
        {
            channelLock.Release();
        }
    }

    private ulong? GetLoggingChannelId(ulong guildId)
    {
        if (_config.LoggingGuilds == null) return null;

        foreach (var kvp in _config.LoggingGuilds.Where(kvp => kvp.Value.GuildId == guildId))
            return kvp.Value.ChannelId;

        return null;
    }

    // --- Event Handlers ---

    public async Task HandleMessageUpdatedAsync(Cacheable<IMessage, ulong> beforeCache, SocketMessage after,
        ISocketMessageChannel channel)
    {
        if (after.Author.IsBot || after.Author.Id == _config.Client.OwnerId) return;
        if (channel is not SocketGuildChannel guildChannel) return;

        var loggingChannelId = GetLoggingChannelId(guildChannel.Guild.Id);
        if (loggingChannelId == null) return;

        var before = await beforeCache.GetOrDownloadAsync();
        if (before == null || before.Content == after.Content) return;

        var webhookClient = await GetWebhookAsync(loggingChannelId.Value);
        if (webhookClient == null) return;

        var author = after.Author;
        var embed = new EmbedBuilder()
            .WithTitle("Message Edit")
            .WithDescription($"in <#{guildChannel.Id}>")
            .WithColor(Color.Red)
            .AddField("Original Message",
                string.IsNullOrWhiteSpace(before.Content) ? "*(Empty)*" :
                before.Content.Length > 1024 ? before.Content.Substring(0, 1021) + "..." : before.Content)
            .AddField("Altered Message",
                string.IsNullOrWhiteSpace(after.Content) ? "*(Empty)*" :
                after.Content.Length > 1024 ? after.Content[..1021] + "..." : after.Content)
            .WithFooter($"{DateTime.Now:h:mm tt, dd MMM}")
            .Build();

        try
        {
            await webhookClient.SendMessageAsync(
                embeds: [embed],
                username: author is SocketGuildUser guildUser ? guildUser.DisplayName : author.Username,
                avatarUrl: author.GetAvatarUrl() ?? author.GetDefaultAvatarUrl()
            );
            _logger.LogInformation("[MESSAGE EDIT] @{User} in #{Channel}", author, guildChannel.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send message edit log via webhook for User {UserId} in Channel {ChannelId}.", author.Id,
                guildChannel.Id);
        }
    }

    public async Task HandleMessageDeletedAsync(Cacheable<IMessage, ulong> messageCache,
        Cacheable<IMessageChannel, ulong> channelCache)
    {
        if (!channelCache.HasValue || channelCache.Value is not SocketGuildChannel guildChannel) return;

        if (guildChannel.Guild.Id != _config.Client.HomeGuildId) return;

        var loggingChannelId = GetLoggingChannelId(guildChannel.Guild.Id);
        if (loggingChannelId == null) return;

        var message = await messageCache.GetOrDownloadAsync();
        if (message == null || message.Author.IsBot || message.Author.Id == _config.Client.OwnerId) return;

        var webhookClient = await GetWebhookAsync(loggingChannelId.Value);
        if (webhookClient == null) return;

        var author = message.Author;
        var embedBuilder = new EmbedBuilder()
            .WithTitle("Deleted Message")
            .WithDescription($"<#{guildChannel.Id}>")
            .WithColor(Color.Red)
            .AddField("Message Content",
                string.IsNullOrWhiteSpace(message.Content) ? "*(Empty)*" :
                message.Content.Length > 1024 ? message.Content[..1021] + "..." : message.Content)
            .WithFooter($"{DateTime.Now:h:mm tt, dd MMM}");

        if (message.Attachments.Count != 0)
            embedBuilder.AddField("Attachments", string.Join("\n", message.Attachments.Select(a => a.Url)));

        try
        {
            await webhookClient.SendMessageAsync(
                embeds: [embedBuilder.Build()],
                username: author is SocketGuildUser guildUser ? guildUser.DisplayName : author.Username,
                avatarUrl: author.GetAvatarUrl() ?? author.GetDefaultAvatarUrl()
            );
            _logger.LogInformation("[MESSAGE DELETE] @{User} in #{Channel}\n\tMessage: {Content}", author,
                guildChannel.Name, message.Content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send message delete log via webhook for Message {MessageId} in Channel {ChannelId}.",
                message.Id, guildChannel.Id);
        }
    }

    public async Task HandleGuildMemberUpdatedAsync(Cacheable<SocketGuildUser, ulong> beforeCache,
        SocketGuildUser after)
    {
        if (after.IsBot || after.Id == _config.Client.OwnerId) return;

        var before = beforeCache.HasValue ? beforeCache.Value : null;
        if (before == null || before.DisplayName == after.DisplayName) return;

        var loggingChannelId = GetLoggingChannelId(after.Guild.Id);
        if (loggingChannelId == null) return;

        var webhookClient = await GetWebhookAsync(loggingChannelId.Value);
        if (webhookClient == null) return;

        var embed = new EmbedBuilder()
            .WithTitle("Nickname Update")
            .WithColor(Color.Red)
            .AddField("Old Name", before.DisplayName)
            .AddField("New Name", after.DisplayName)
            .WithFooter($"{DateTime.Now:h:mm tt, dd MMM}")
            .Build();

        try
        {
            await webhookClient.SendMessageAsync(
                embeds: [embed],
                username: after.DisplayName,
                avatarUrl: after.GetAvatarUrl() ?? after.GetDefaultAvatarUrl()
            );
            _logger.LogInformation("[UPDATE] Nickname {GuildName}: @{OldName} -> @{NewName}", after.Guild.Name,
                before.DisplayName, after.DisplayName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send nickname update log via webhook for User {UserId} in Guild {GuildId}.",
                after.Id, after.Guild.Id);
        }
    }

    public async Task HandleUserUpdatedAsync(SocketUser before, SocketUser after)
    {
        if (after.IsBot || after.Id == _config.Client.OwnerId) return;
        if (before.Username == after.Username && before.Discriminator == after.Discriminator) return;

        foreach (var guild in _client.Guilds)
        {
            var loggingChannelId = GetLoggingChannelId(guild.Id);
            if (loggingChannelId == null) continue;

            var member = guild.GetUser(after.Id);
            if (member == null) continue;

            var webhookClient = await GetWebhookAsync(loggingChannelId.Value);
            if (webhookClient == null) continue;

            var embed = new EmbedBuilder()
                .WithAuthor("Username Change", before.GetAvatarUrl() ?? before.GetDefaultAvatarUrl())
                .WithColor(Color.Red)
                .AddField("Old Username", before.ToString())
                .AddField("New Username", after.ToString())
                .WithFooter($"{DateTime.Now:h:mm tt, dd MMM}")
                .Build();

            try
            {
                await webhookClient.SendMessageAsync(
                    embeds: [embed],
                    username: member.DisplayName,
                    avatarUrl: after.GetAvatarUrl() ?? after.GetDefaultAvatarUrl()
                );
                _logger.LogInformation("[UPDATE] Username {GuildName}: @{BeforeUser} -> @{AfterUser}", guild.Name,
                    before, after);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to send username update log via webhook for User {UserId} in Guild {GuildId}.", after.Id,
                    guild.Id);
            }
        }
    }

    public async Task HandlePresenceUpdatedAsync(SocketUser user, SocketPresence before, SocketPresence after)
    {
        if (user.IsBot || user is not SocketGuildUser guildUser) return;
        if (guildUser.Guild.Id != _config.Client.HomeGuildId) return;

        var loggingChannelId = GetLoggingChannelId(guildUser.Guild.Id);
        if (loggingChannelId == null) return;

        var bClients = ActivityUtils.GetClients(before);
        var aClients = ActivityUtils.GetClients(after);
        var bStatus = before.Status.ToString().ToLowerInvariant();
        var aStatus = after.Status.ToString().ToLowerInvariant();

        // Check if status or client list actually changed
        if (bStatus == aStatus && bClients.SetEquals(aClients) &&
            before.Activities.SequenceEqual(after.Activities)) return;

        var webhookClient = await GetWebhookAsync(loggingChannelId.Value);
        if (webhookClient == null) return;

        var statusSummary = ActivityUtils.SummarizeStatusChange(bClients, bStatus, aClients, aStatus);
        List<string> logMessages = [];
        if (statusSummary != null) logMessages.Add(statusSummary);

        _logger.LogInformation("[UPDATE] Presence @{User} from {GuildName} {Summary}", user, guildUser.Guild.Name,
            statusSummary);

        // Log detailed activity changes only if not going offline/coming online
        if (bStatus != "offline" || aStatus != "offline")
        {
            var bActivities = ActivityUtils.GetAllUserActivities(before.Activities, false, true, true);
            var aActivities = ActivityUtils.GetAllUserActivities(after.Activities, false, true, true);
            var allKeys = bActivities.Keys.Union(aActivities.Keys).ToHashSet();

            foreach (var key in allKeys.Where(key => key != "Spotify"))
            {
                bActivities.TryGetValue(key, out var bValue);
                aActivities.TryGetValue(key, out var aValue);

                if (bValue == aValue) continue;

                var change = "";
                if (key == "Custom Status")
                    switch (string.IsNullOrEmpty(bValue))
                    {
                        case false when !string.IsNullOrEmpty(aValue):
                            change = $"Custom Status: {bValue} -> {aValue}";
                            break;
                        case false when string.IsNullOrEmpty(aValue):
                            change = $"Removed Custom Status: {bValue}";
                            break;
                        default:
                        {
                            if (string.IsNullOrEmpty(bValue) && !string.IsNullOrEmpty(aValue))
                                change = $"Custom Status: {aValue}";
                            break;
                        }
                    }
                else if (string.IsNullOrEmpty(bValue))
                    change = $"Started {key}: {aValue}";
                else if (string.IsNullOrEmpty(aValue))
                    change = $"Stopped {key}: {bValue}";
                else
                    change = $"{key}: {bValue} -> {aValue}";

                if (string.IsNullOrEmpty(change)) continue;
                logMessages.Add(change);
                _logger.LogInformation("[UPDATE] Presence Activity {GuildName}: @{User} {Change}",
                    guildUser.Guild.Name, user, change);
            }
        }

        var messageContent = string.Join("\n", logMessages).Trim();
        if (string.IsNullOrEmpty(messageContent)) return;

        try
        {
            await webhookClient.SendMessageAsync(
                messageContent,
                username: guildUser.DisplayName,
                avatarUrl: user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl(),
                allowedMentions: AllowedMentions.None,
                flags: MessageFlags.SuppressEmbeds
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send presence update log via webhook for User {UserId}.", user.Id);
        }
    }

    public async Task HandleVoiceStateUpdatedAsync(SocketUser user, SocketVoiceState before, SocketVoiceState after)
    {
        if (user.IsBot || user.Id == _config.Client.OwnerId || user is not SocketGuildUser member) return;

        if (before.VoiceChannel?.Id == after.VoiceChannel?.Id) return;

        var loggingChannelId = GetLoggingChannelId(member.Guild.Id);
        if (loggingChannelId == null) return;

        var webhookClient = await GetWebhookAsync(loggingChannelId.Value);
        if (webhookClient == null) return;

        string msg;
        string logMsg;

        if (after.VoiceChannel != null && before.VoiceChannel == null)
        {
            msg = $"🏞️ -> {after.VoiceChannel.Mention}";
            logMsg = $"🏞️ -> #{after.VoiceChannel.Name}";
        }
        else if (before.VoiceChannel != null && after.VoiceChannel == null)
        {
            msg = $"{before.VoiceChannel.Mention} -> 🏞️";
            logMsg = $"#{before.VoiceChannel.Name} -> 🏞️";
        }
        else if (before.VoiceChannel != null && after.VoiceChannel != null)
        {
            msg = $"{before.VoiceChannel.Mention} -> {after.VoiceChannel.Mention}";
            logMsg = $"#{before.VoiceChannel.Name} -> #{after.VoiceChannel.Name}";
        }
        else
        {
            return;
        }

        try
        {
            await webhookClient.SendMessageAsync(
                msg,
                username: member.DisplayName,
                avatarUrl: user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl(),
                allowedMentions: AllowedMentions.None
            );
            _logger.LogInformation("[UPDATE] Voice {GuildName}: @{User}: {LogMsg}", member.Guild.Name,
                member.DisplayName, logMsg);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send voice state update log via webhook for User {UserId}.", user.Id);
        }
    }

    public async Task HandleTypingAsync(Cacheable<IUser, ulong> userCache,
        Cacheable<IMessageChannel, ulong> channelCache)
    {
        var user = await userCache.GetOrDownloadAsync();
        var channel = await channelCache.GetOrDownloadAsync();

        if (user.IsBot || user.Id == _config.Client.OwnerId) return;
        if (channel is not SocketGuildChannel guildChannel) return;

        if (guildChannel.Guild.Id != _config.Client.HomeGuildId) return;

        _logger.LogInformation("[UPDATE] Typing @{User} - #{Channel} on {Timestamp}",
            user, guildChannel.Name, DateTime.UtcNow.ToString("dd/MM/yyyy 'at' hh:mm:ss tt"));
    }

    public async Task HandleUserLeftAsync(SocketGuild guild, SocketUser user)
    {
        if (user.IsBot) return;

        if (guild.Id != _config.Client.HomeGuildId) return;

        var loggingChannelId = GetLoggingChannelId(guild.Id);
        if (loggingChannelId == null) return;

        var webhookClient = await GetWebhookAsync(loggingChannelId.Value);
        if (webhookClient == null) return;

        try
        {
            await webhookClient.SendMessageAsync(
                $"{user.Username} left the server.",
                username: user.Username,
                avatarUrl: user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl()
            );
            _logger.LogInformation("[GUILD] Leave @{User}: {GuildName}", user.Username, guild.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send user left log via webhook for User {UserId} in Guild {GuildId}.",
                user.Id, guild.Id);
        }
    }

    public async Task HandleUserJoinedAsync(SocketGuildUser member)
    {
        if (member.IsBot) return;

        if (member.Guild.Id != _config.Client.HomeGuildId) return;

        var loggingChannelId = GetLoggingChannelId(member.Guild.Id);
        if (loggingChannelId == null) return;

        var webhookClient = await GetWebhookAsync(loggingChannelId.Value);
        if (webhookClient == null) return;

        try
        {
            await webhookClient.SendMessageAsync(
                $"{member.DisplayName} joined the server",
                username: member.DisplayName,
                avatarUrl: member.GetAvatarUrl() ?? member.GetDefaultAvatarUrl()
            );
            _logger.LogInformation("[GUILD] Join @{User}: {GuildName}", member.Username, member.Guild.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send user joined log via webhook for User {UserId} in Guild {GuildId}.",
                member.Id, member.Guild.Id);
        }
    }

    public async Task HandleUserBannedAsync(SocketUser user, SocketGuild guild)
    {
        if (user.IsBot) return;

        if (guild.Id != _config.Client.HomeGuildId) return;

        var loggingChannelId = GetLoggingChannelId(guild.Id);
        if (loggingChannelId == null) return;

        var webhookClient = await GetWebhookAsync(loggingChannelId.Value);
        if (webhookClient == null) return;

        try
        {
            await webhookClient.SendMessageAsync(
                $"{user.Username} was banned from the server",
                username: user.Username,
                avatarUrl: user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl()
            );
            _logger.LogInformation("[GUILD] Ban @{User}: {GuildName}", user.Username, guild.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send user banned log via webhook for User {UserId} in Guild {GuildId}.",
                user.Id, guild.Id);
        }
    }

    public async Task HandleUserUnbannedAsync(SocketUser user, SocketGuild guild)
    {
        if (user.IsBot) return;

        if (guild.Id != _config.Client.HomeGuildId) return;

        var loggingChannelId = GetLoggingChannelId(guild.Id);
        if (loggingChannelId == null) return;

        var webhookClient = await GetWebhookAsync(loggingChannelId.Value);
        if (webhookClient == null) return;

        try
        {
            await webhookClient.SendMessageAsync(
                $"{user.Username} was unbanned from the server",
                username: user.Username,
                avatarUrl: user.GetAvatarUrl() ?? user.GetDefaultAvatarUrl()
            );
            _logger.LogInformation("[GUILD] Unban @{User}: {GuildName}", user.Username, guild.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send user unbanned log via webhook for User {UserId} in Guild {GuildId}.",
                user.Id, guild.Id);
        }
    }
}