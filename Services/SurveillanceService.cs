using System.Text;
using Assistant.Net.Configuration;
using Assistant.Net.Utilities;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Services;

public class SurveillanceService
{
    private readonly DiscordSocketClient _client;
    private readonly Config _config;
    private readonly ILogger<SurveillanceService> _logger;
    private readonly WebhookService _webhookService;

    public SurveillanceService(
        DiscordSocketClient client,
        Config config,
        ILogger<SurveillanceService> logger,
        WebhookService webhookService)
    {
        _client = client;
        _config = config;
        _logger = logger;
        _webhookService = webhookService;

        _client.MessageUpdated += HandleMessageUpdatedAsync;
        _client.MessageDeleted += HandleMessageDeletedAsync;
        _client.GuildMemberUpdated += HandleGuildMemberUpdatedAsync;
        _client.UserUpdated += HandleUserUpdatedAsync;
        _client.PresenceUpdated += HandlePresenceUpdatedAsync;
        _client.UserVoiceStateUpdated += HandleVoiceStateUpdatedAsync;
        _client.UserIsTyping += HandleTypingAsync;
        _client.UserJoined += HandleUserJoinedAsync;
        _client.UserLeft += HandleUserLeftAsync;
        _client.UserBanned += HandleUserBannedAsync;
        _client.UserUnbanned += HandleUserUnbannedAsync;

        _logger.LogInformation("SurveillanceService initialized and events hooked.");
    }

    private ulong? GetLoggingChannelId(ulong guildId)
    {
        if (_config.LoggingGuilds == null) return null;

        return _config.LoggingGuilds
            .FirstOrDefault(kvp => kvp.Value.GuildId == guildId)
            .Value?.ChannelId;
    }

    // --- Event Handlers ---

    public async Task HandleMessageUpdatedAsync(Cacheable<IMessage, ulong> beforeCache, SocketMessage after,
        ISocketMessageChannel channel)
    {
        if (after.Author.IsBot ||
            (_config.Client.OwnerId.HasValue && after.Author.Id == _config.Client.OwnerId.Value)) return;
        if (channel is not SocketGuildChannel guildChannel) return;

        var loggingChannelId = GetLoggingChannelId(guildChannel.Guild.Id);
        if (loggingChannelId == null) return;

        var before = await beforeCache.GetOrDownloadAsync();
        if (before == null || before.Content == after.Content) return;

        var webhookClient = await _webhookService.GetOrCreateWebhookClientAsync(loggingChannelId.Value);
        if (webhookClient == null) return;

        var author = after.Author;
        var embed = new EmbedBuilder()
            .WithTitle("Message Edit")
            .WithDescription($"in <#{guildChannel.Id}> ({after.GetJumpUrl()})")
            .WithColor(Color.Orange)
            .AddField("Original Message",
                string.IsNullOrWhiteSpace(before.Content) ? "*(Empty)*" : before.Content.Truncate(1024))
            .AddField("Altered Message",
                string.IsNullOrWhiteSpace(after.Content) ? "*(Empty)*" : after.Content.Truncate(1024))
            .WithFooter($"User ID: {author.Id}")
            .WithTimestamp(after.EditedTimestamp ?? after.Timestamp)
            .Build();

        try
        {
            await webhookClient.SendMessageAsync(
                embeds: [embed],
                username: author is SocketGuildUser sgu ? sgu.DisplayName : author.Username,
                avatarUrl: author.GetDisplayAvatarUrl() ?? author.GetDefaultAvatarUrl()
            );
            _logger.LogInformation("[MESSAGE EDIT] @{User} in #{Channel}", author.Username, guildChannel.Name);
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
        var channel = channelCache.HasValue ? channelCache.Value : await channelCache.GetOrDownloadAsync();
        if (channel is not SocketGuildChannel guildChannel) return;

        var loggingChannelId = GetLoggingChannelId(guildChannel.Guild.Id);
        if (loggingChannelId == null) return;

        var message = await messageCache.GetOrDownloadAsync();
        if (message == null || message.Author.IsBot ||
            (_config.Client.OwnerId.HasValue && message.Author.Id == _config.Client.OwnerId.Value)) return;

        var webhookClient = await _webhookService.GetOrCreateWebhookClientAsync(loggingChannelId.Value);
        if (webhookClient == null) return;

        var author = message.Author;
        var embedBuilder = new EmbedBuilder()
            .WithTitle("Message Deleted")
            .WithDescription($"in <#{guildChannel.Id}>")
            .WithColor(Color.Red)
            .AddField("Message Content",
                string.IsNullOrWhiteSpace(message.Content) ? "*(Empty)*" : message.Content.Truncate(1024))
            .WithFooter($"User ID: {author.Id} | Message ID: {message.Id}")
            .WithTimestamp(message.Timestamp);

        if (message.Attachments.Count != 0)
            embedBuilder.AddField("Attachments",
                string.Join("\n", message.Attachments.Select(a => $"[{a.Filename}]({a.Url}) (Size: {a.Size} bytes)")));

        try
        {
            await webhookClient.SendMessageAsync(
                embeds: [embedBuilder.Build()],
                username: author is SocketGuildUser sgu ? sgu.DisplayName : author.Username,
                avatarUrl: author.GetDisplayAvatarUrl() ?? author.GetDefaultAvatarUrl()
            );
            _logger.LogInformation("[MESSAGE DELETE] @{User} in #{Channel}\n\tMessage: {Content}", author.Username,
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
        if (after.IsBot || (_config.Client.OwnerId.HasValue && after.Id == _config.Client.OwnerId.Value)) return;

        var before = beforeCache.HasValue ? beforeCache.Value : null;
        if (before == null || before.DisplayName == after.DisplayName) return;

        var loggingChannelId = GetLoggingChannelId(after.Guild.Id);
        if (loggingChannelId == null) return;

        var webhookClient = await _webhookService.GetOrCreateWebhookClientAsync(loggingChannelId.Value);
        if (webhookClient == null) return;

        var embed = new EmbedBuilder()
            .WithTitle("Member Update: Nickname Change")
            .WithDescription($"{after.Mention} ({after.Username}#{after.Discriminator})")
            .WithColor(Color.LightOrange)
            .AddField("Old Nickname", string.IsNullOrWhiteSpace(before.Nickname) ? "*(None)*" : before.DisplayName,
                true)
            .AddField("New Nickname", string.IsNullOrWhiteSpace(after.Nickname) ? "*(None)*" : after.DisplayName, true)
            .WithFooter($"User ID: {after.Id}")
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build();

        try
        {
            await webhookClient.SendMessageAsync(
                embeds: [embed],
                username: "Member Update Logger",
                avatarUrl: after.GetDisplayAvatarUrl() ?? after.GetDefaultAvatarUrl()
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
        if (after.IsBot || (_config.Client.OwnerId.HasValue && after.Id == _config.Client.OwnerId.Value)) return;
        if (before.Username == after.Username &&
            before.Discriminator == after.Discriminator &&
            before.GetDisplayAvatarUrl() == after.GetDisplayAvatarUrl()) return;

        foreach (var guild in _client.Guilds)
        {
            var loggingChannelId = GetLoggingChannelId(guild.Id);
            if (loggingChannelId == null) continue;

            var member = guild.GetUser(after.Id);
            if (member == null) continue;

            var webhookClient = await _webhookService.GetOrCreateWebhookClientAsync(loggingChannelId.Value);
            if (webhookClient == null) continue;

            var embed = new EmbedBuilder()
                .WithTitle("User Profile Update")
                .WithDescription($"{after.Mention} ({after.Username}#{after.Discriminator})")
                .WithColor(Color.Blue)
                .WithThumbnailUrl(after.GetDisplayAvatarUrl() ?? after.GetDefaultAvatarUrl())
                .WithFooter($"User ID: {after.Id}")
                .WithTimestamp(DateTimeOffset.UtcNow);

            if (before.Username != after.Username || before.Discriminator != after.Discriminator)
                embed.AddField("Username Change",
                    $"`{before.Username}#{before.Discriminator}` → `{after.Username}#{after.Discriminator}`");
            if (before.GetDisplayAvatarUrl() != after.GetDisplayAvatarUrl())
            {
                embed.AddField("Avatar Changed",
                    $"[Before]({before.GetDisplayAvatarUrl() ?? before.GetDefaultAvatarUrl()}) → [After]({after.GetDisplayAvatarUrl() ?? after.GetDefaultAvatarUrl()})");
                embed.WithImageUrl(after.GetDisplayAvatarUrl() ?? after.GetDefaultAvatarUrl());
            }


            try
            {
                await webhookClient.SendMessageAsync(
                    embeds: [embed.Build()],
                    username: "User Profile Logger",
                    avatarUrl: _client.CurrentUser.GetDisplayAvatarUrl() ?? _client.CurrentUser.GetDefaultAvatarUrl()
                );
                _logger.LogInformation("[UPDATE] User Profile {GuildName}: @{BeforeUser} -> @{AfterUser}", guild.Name,
                    before, after);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to send user profile update log via webhook for User {UserId} in Guild {GuildId}.",
                    after.Id,
                    guild.Id);
            }
        }
    }

    public async Task HandlePresenceUpdatedAsync(SocketUser user, SocketPresence before, SocketPresence after)
    {
        if (user.IsBot || user is not SocketGuildUser guildUser) return;

        var loggingChannelId = GetLoggingChannelId(guildUser.Guild.Id);
        if (loggingChannelId == null) return;

        var bClients = ActivityUtils.GetClients(before);
        var aClients = ActivityUtils.GetClients(after);
        var bStatus = before.Status.ToString().ToLowerInvariant();
        var aStatus = after.Status.ToString().ToLowerInvariant();

        if (bStatus == aStatus &&
            bClients.SetEquals(aClients) &&
            before.Activities.SequenceEqual(after.Activities, ActivityComparer.Instance)) return;


        var webhookClient = await _webhookService.GetOrCreateWebhookClientAsync(loggingChannelId.Value);
        if (webhookClient == null) return;

        var statusSummary = ActivityUtils.SummarizeStatusChange(bClients, bStatus, aClients, aStatus);
        var logParts = new List<string>();
        if (statusSummary != null) logParts.Add(statusSummary);

        _logger.LogInformation("[UPDATE] Presence @{User} from {GuildName} {Summary}", user.Username,
            guildUser.Guild.Name,
            statusSummary ?? "No direct status/client change.");

        var bActivities = ActivityUtils.GetAllUserActivities(before.Activities, false, true, true);
        var aActivities = ActivityUtils.GetAllUserActivities(after.Activities, false, true, true);
        var allActivityKeys = bActivities.Keys.Union(aActivities.Keys).ToHashSet();

        var activityChanged = false;
        foreach (var key in allActivityKeys)
        {
            bActivities.TryGetValue(key, out var bValue);
            aActivities.TryGetValue(key, out var aValue);

            if (bValue == aValue) continue;
            activityChanged = true;

            var changeDescription = "";
            if (key == "Custom Status")
            {
                if (!string.IsNullOrEmpty(bValue) && !string.IsNullOrEmpty(aValue))
                    changeDescription = $"Custom Status: `{bValue}` → `{aValue}`";
                else if (!string.IsNullOrEmpty(bValue)) changeDescription = $"Removed Custom Status: `{bValue}`";
                else if (!string.IsNullOrEmpty(aValue)) changeDescription = $"Set Custom Status: `{aValue}`";
            }
            else
            {
                if (string.IsNullOrEmpty(bValue)) changeDescription = $"Started {key}: `{aValue}`";
                else if (string.IsNullOrEmpty(aValue)) changeDescription = $"Stopped {key}: `{bValue}`";
                else changeDescription = $"{key}: `{bValue}` → `{aValue}`";
            }

            if (!string.IsNullOrEmpty(changeDescription)) logParts.Add(changeDescription);
        }

        if (statusSummary == null && !activityChanged) return;

        var messageContent = string.Join("\n", logParts).Trim();
        if (string.IsNullOrEmpty(messageContent)) return;

        try
        {
            await webhookClient.SendMessageAsync(
                messageContent.Truncate(DiscordConfig.MaxMessageSize),
                username: guildUser.DisplayName,
                avatarUrl: user.GetDisplayAvatarUrl() ?? user.GetDefaultAvatarUrl(),
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
        if (user.IsBot || (_config.Client.OwnerId.HasValue && user.Id == _config.Client.OwnerId.Value) ||
            user is not SocketGuildUser member) return;

        // Log only if channel actually changed
        if (before.VoiceChannel?.Id == after.VoiceChannel?.Id &&
            before.IsMuted == after.IsMuted &&
            before.IsDeafened == after.IsDeafened &&
            before.IsSelfMuted == after.IsSelfMuted &&
            before.IsSelfDeafened == after.IsSelfDeafened &&
            before.IsStreaming == after.IsStreaming &&
            before.IsVideoing == after.IsVideoing &&
            before.IsSuppressed == after.IsSuppressed)
            return;

        var loggingChannelId = GetLoggingChannelId(member.Guild.Id);
        if (loggingChannelId == null) return;

        var webhookClient = await _webhookService.GetOrCreateWebhookClientAsync(loggingChannelId.Value);
        if (webhookClient == null) return;

        var embed = new EmbedBuilder()
            .WithAuthor(member.DisplayName, member.GetDisplayAvatarUrl() ?? member.GetDefaultAvatarUrl())
            .WithColor(Color.DarkGreen)
            .WithFooter($"User ID: {member.Id}")
            .WithTimestamp(DateTimeOffset.UtcNow);

        var actionDescription = new StringBuilder();

        if (before.VoiceChannel?.Id != after.VoiceChannel?.Id)
        {
            if (after.VoiceChannel != null && before.VoiceChannel == null)
                actionDescription.AppendLine(
                    $"➡️ Joined voice channel {after.VoiceChannel.Mention} (`{after.VoiceChannel.Name}`).");
            else if (before.VoiceChannel != null && after.VoiceChannel == null)
                actionDescription.AppendLine(
                    $"⬅️ Left voice channel {before.VoiceChannel.Mention} (`{before.VoiceChannel.Name}`).");
            else if (before.VoiceChannel != null && after.VoiceChannel != null)
                actionDescription.AppendLine(
                    $"🔄 Switched voice channel from {before.VoiceChannel.Mention} to {after.VoiceChannel.Mention}.");
        }

        // Detailed state changes
        if (before.IsMuted != after.IsMuted)
            actionDescription.AppendLine(after.IsMuted ? "🔇 Server Muted" : "🔊 Server Unmuted");
        if (before.IsDeafened != after.IsDeafened)
            actionDescription.AppendLine(after.IsDeafened ? "🔇 Server Deafened" : "🔊 Server Undeafened");
        if (before.IsSelfMuted != after.IsSelfMuted)
            actionDescription.AppendLine(after.IsSelfMuted ? "🎙️ Self-Muted" : "🎤 Self-Unmuted");
        if (before.IsSelfDeafened != after.IsSelfDeafened)
            actionDescription.AppendLine(after.IsSelfDeafened ? "🎧 Self-Deafened" : "🎶 Self-Undeafened");
        if (before.IsStreaming != after.IsStreaming)
            actionDescription.AppendLine(after.IsStreaming ? "🖥️ Started Streaming" : "🛑 Stopped Streaming");
        if (before.IsVideoing != after.IsVideoing)
            actionDescription.AppendLine(after.IsVideoing ? "📹 Camera On" : "🚫 Camera Off");

        if (actionDescription.Length == 0) return; // No loggable change

        embed.WithDescription(actionDescription.ToString());

        try
        {
            await webhookClient.SendMessageAsync(
                embeds: [embed.Build()],
                username: "Voice State Logger",
                avatarUrl: _client.CurrentUser.GetDisplayAvatarUrl() ?? _client.CurrentUser.GetDefaultAvatarUrl(),
                allowedMentions: AllowedMentions.None
            );
            _logger.LogInformation("[UPDATE] Voice {GuildName}: @{User}: {Action}", member.Guild.Name,
                member.Username, actionDescription.ToString().Replace("\n", " "));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send voice state update log via webhook for User {UserId}.", user.Id);
        }
    }

    public Task HandleTypingAsync(Cacheable<IUser, ulong> userCache,
        Cacheable<IMessageChannel, ulong> channelCache) =>
        // Typing logs can be very noisy.
        /*
        var user = await userCache.GetOrDownloadAsync();
        var channel = await channelCache.GetOrDownloadAsync();

        if (user.IsBot || (_config.Client.OwnerId.HasValue && user.Id == _config.Client.OwnerId.Value)) return Task.CompletedTask;
        if (channel is not SocketGuildChannel guildChannel) return Task.CompletedTask;

        _logger.LogTrace("[TYPING] @{User} - #{Channel} on {Timestamp}",
            user.Username, guildChannel.Name, DateTime.UtcNow.ToString("dd/MM/yyyy 'at' hh:mm:ss tt"));
        */
        Task.CompletedTask;

    public async Task HandleUserLeftAsync(SocketGuild guild, SocketUser user)
    {
        if (user.IsBot) return;

        var loggingChannelId = GetLoggingChannelId(guild.Id);
        if (loggingChannelId == null) return;

        var webhookClient = await _webhookService.GetOrCreateWebhookClientAsync(loggingChannelId.Value);
        if (webhookClient == null) return;

        var embed = new EmbedBuilder()
            .WithAuthor($"{user.Username}#{user.Discriminator} Left",
                user.GetDisplayAvatarUrl() ?? user.GetDefaultAvatarUrl())
            .WithDescription($"{user.Mention} has left the server.")
            .WithColor(Color.DarkGrey)
            .WithFooter($"User ID: {user.Id}")
            .WithTimestamp(DateTimeOffset.UtcNow);

        try
        {
            await webhookClient.SendMessageAsync(
                embeds: [embed.Build()],
                username: "Join/Leave Logger",
                avatarUrl: _client.CurrentUser.GetDisplayAvatarUrl() ?? _client.CurrentUser.GetDefaultAvatarUrl()
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

        var loggingChannelId = GetLoggingChannelId(member.Guild.Id);
        if (loggingChannelId == null) return;

        var webhookClient = await _webhookService.GetOrCreateWebhookClientAsync(loggingChannelId.Value);
        if (webhookClient == null) return;

        var embed = new EmbedBuilder()
            .WithAuthor($"{member.Username}#{member.Discriminator} Joined",
                member.GetDisplayAvatarUrl() ?? member.GetDefaultAvatarUrl())
            .WithDescription($"{member.Mention} has joined the server.")
            .WithColor(Color.Green)
            .AddField("Account Created",
                TimestampTag.FromDateTime(member.CreatedAt.DateTime, TimestampTagStyles.LongDateTime) + " (" +
                TimestampTag.FromDateTime(member.CreatedAt.DateTime, TimestampTagStyles.Relative) + ")")
            .WithFooter($"User ID: {member.Id}")
            .WithTimestamp(member.JoinedAt ?? DateTimeOffset.UtcNow);


        try
        {
            await webhookClient.SendMessageAsync(
                embeds: [embed.Build()],
                username: "Join/Leave Logger",
                avatarUrl: _client.CurrentUser.GetDisplayAvatarUrl() ?? _client.CurrentUser.GetDefaultAvatarUrl()
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

        var loggingChannelId = GetLoggingChannelId(guild.Id);
        if (loggingChannelId == null) return;

        var webhookClient = await _webhookService.GetOrCreateWebhookClientAsync(loggingChannelId.Value);
        if (webhookClient == null) return;

        var banReason = "Not specified";
        try
        {
            var ban = await guild.GetBanAsync(user);
            if (ban != null && !string.IsNullOrWhiteSpace(ban.Reason)) banReason = ban.Reason;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not fetch ban reason for user {UserId} in guild {GuildId}", user.Id,
                guild.Id);
        }

        var embed = new EmbedBuilder()
            .WithAuthor($"{user.Username}#{user.Discriminator} Banned",
                user.GetDisplayAvatarUrl() ?? user.GetDefaultAvatarUrl())
            .WithDescription($"{user.Mention} was banned from the server.")
            .AddField("Reason", banReason)
            .WithColor(Color.DarkRed)
            .WithFooter($"User ID: {user.Id}")
            .WithTimestamp(DateTimeOffset.UtcNow);

        try
        {
            await webhookClient.SendMessageAsync(
                embeds: [embed.Build()],
                username: "Moderation Logger",
                avatarUrl: _client.CurrentUser.GetDisplayAvatarUrl() ?? _client.CurrentUser.GetDefaultAvatarUrl()
            );
            _logger.LogInformation("[GUILD] Ban @{User}: {GuildName}. Reason: {Reason}", user.Username, guild.Name,
                banReason);
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

        var loggingChannelId = GetLoggingChannelId(guild.Id);
        if (loggingChannelId == null) return;

        var webhookClient = await _webhookService.GetOrCreateWebhookClientAsync(loggingChannelId.Value);
        if (webhookClient == null) return;

        var embed = new EmbedBuilder()
            .WithAuthor($"{user.Username}#{user.Discriminator} Unbanned",
                user.GetDisplayAvatarUrl() ?? user.GetDefaultAvatarUrl())
            .WithDescription($"{user.Mention} was unbanned from the server.")
            .WithColor(Color.DarkGreen)
            .WithFooter($"User ID: {user.Id}")
            .WithTimestamp(DateTimeOffset.UtcNow);

        try
        {
            await webhookClient.SendMessageAsync(
                embeds: [embed.Build()],
                username: "Moderation Logger",
                avatarUrl: _client.CurrentUser.GetDisplayAvatarUrl() ?? _client.CurrentUser.GetDefaultAvatarUrl()
            );
            _logger.LogInformation("[GUILD] Unban @{User}: {GuildName}", user.Username, guild.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send user unbanned log via webhook for User {UserId} in Guild {GuildId}.",
                user.Id, guild.Id);
        }
    }

    private class ActivityComparer : IEqualityComparer<IActivity>
    {
        public static readonly ActivityComparer Instance = new();

        public bool Equals(IActivity? x, IActivity? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;
            if (x.Type != y.Type) return false;
            if (x.Name != y.Name) return false;

            return x switch
            {
                CustomStatusGame csgX when y is CustomStatusGame csgY => csgX.State == csgY.State &&
                                                                         csgX.Emote?.Name == csgY.Emote?.Name &&
                                                                         (csgX.Emote as GuildEmote)?.Id ==
                                                                         (csgY.Emote as GuildEmote)?.Id,
                RichGame rgX when y is RichGame rgY => rgX.Details == rgY.Details && rgX.State == rgY.State,
                _ => true
            };
        }

        public int GetHashCode(IActivity obj) => HashCode.Combine(obj.Type, obj.Name);
    }
}