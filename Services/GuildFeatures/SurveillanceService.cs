using System.Text;
using Assistant.Net.Configuration;
using Assistant.Net.Services.Core;
using Assistant.Net.Utilities;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Services.GuildFeatures;

public class SurveillanceService
{
    private const int DeleteDelay = 24 * 60 * 60 * 1000; // one day
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
        _client.UserJoined += HandleUserJoinedAsync;
        _client.UserLeft += HandleUserLeftAsync;
        _client.UserBanned += HandleUserBannedAsync;
        _client.UserUnbanned += HandleUserUnbannedAsync;

        _logger.LogInformation("SurveillanceService initialized and events hooked.");
    }

    private LoggingGuildConfig? GetLoggingGuildConfig(ulong guildId)
    {
        return _config.LoggingGuilds?.FirstOrDefault(kvp => kvp.Value.GuildId == guildId).Value;
    }


    // --- Component Builders ---

    private static MessageComponent BuildMessageUpdatedComponent(IMessage before, IMessage after,
        SocketGuildChannel guildChannel)
    {
        var container = new ContainerBuilder()
            .WithAccentColor(Color.Orange)
            .WithSection(section =>
            {
                section.AddComponent(new TextDisplayBuilder("# Message Edit"));
                section.AddComponent(new TextDisplayBuilder($"in <#{guildChannel.Id}>"));
                section.WithAccessory(new ThumbnailBuilder
                {
                    Media = new UnfurledMediaItemProperties
                        { Url = after.Author.GetDisplayAvatarUrl() ?? after.Author.GetDefaultAvatarUrl() }
                });
            })
            .WithSeparator()
            .WithTextDisplay(new TextDisplayBuilder(
                $"**Before:**\n> {(string.IsNullOrWhiteSpace(before.Content) ? "*(Empty)*" : before.Content.Truncate(1000))}"))
            .WithTextDisplay(new TextDisplayBuilder(
                $"**After:**\n> {(string.IsNullOrWhiteSpace(after.Content) ? "*(Empty)*" : after.Content.Truncate(1000))}"))
            .WithSeparator()
            .WithTextDisplay(new TextDisplayBuilder(
                $"User ID: {after.Author.Id} | {TimestampTag.FromDateTimeOffset(after.EditedTimestamp ?? after.Timestamp)}"))
            .WithActionRow(row => row.WithButton("Jump to Message", style: ButtonStyle.Link, url: after.GetJumpUrl()));

        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    private static MessageComponent BuildMessageDeletedComponent(IMessage message, SocketGuildChannel guildChannel)
    {
        var container = new ContainerBuilder()
            .WithAccentColor(Color.Red)
            .WithSection(section =>
            {
                section.AddComponent(new TextDisplayBuilder("# Message Deleted"));
                section.AddComponent(new TextDisplayBuilder($"from <#{guildChannel.Id}>"));
                section.WithAccessory(new ThumbnailBuilder
                {
                    Media = new UnfurledMediaItemProperties
                        { Url = message.Author.GetDisplayAvatarUrl() ?? message.Author.GetDefaultAvatarUrl() }
                });
            })
            .WithSeparator()
            .WithTextDisplay(new TextDisplayBuilder(
                $"**Content:**\n> {(string.IsNullOrWhiteSpace(message.Content) ? "*(Empty)*" : message.Content.Truncate(2000))}"
            ));

        if (message.Attachments.Count > 0)
        {
            var attachmentsText = string.Join("\n",
                message.Attachments.Select(a => $"📄 [{a.Filename}]({a.Url}) ({FormatUtils.FormatBytes(a.Size)})"));
            container.WithTextDisplay(new TextDisplayBuilder($"**Attachments:**\n{attachmentsText}"));
        }

        container
            .WithSeparator()
            .WithTextDisplay(new TextDisplayBuilder(
                $"User ID: {message.Author.Id} | Message ID: {message.Id} | {TimestampTag.FromDateTimeOffset(message.Timestamp)}"));

        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    private static MessageComponent BuildNicknameChangeComponent(SocketGuildUser before, SocketGuildUser after)
    {
        var container = new ContainerBuilder()
            .WithAccentColor(Color.LightOrange)
            .WithSection(section =>
            {
                section.AddComponent(new TextDisplayBuilder("# Nickname Changed"));
                section.AddComponent(new TextDisplayBuilder($"{after.Mention}"));
                section.WithAccessory(new ThumbnailBuilder
                {
                    Media = new UnfurledMediaItemProperties
                        { Url = after.GetDisplayAvatarUrl() ?? after.GetDefaultAvatarUrl() }
                });
            })
            .WithSeparator()
            .WithTextDisplay(new TextDisplayBuilder($"**Before:** `{before.DisplayName}`"))
            .WithTextDisplay(new TextDisplayBuilder($"**After:** `{after.DisplayName}`"))
            .WithSeparator()
            .WithTextDisplay(new TextDisplayBuilder(
                $"User ID: {after.Id} | {TimestampTag.FromDateTimeOffset(DateTimeOffset.UtcNow)}"));

        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    private static MessageComponent BuildUserProfileUpdateComponent(SocketUser before, SocketUser after)
    {
        var container = new ContainerBuilder()
            .WithAccentColor(Color.Blue)
            .WithSection(section =>
            {
                section.AddComponent(new TextDisplayBuilder("# User Profile Updated"));
                section.AddComponent(new TextDisplayBuilder($"{after.Mention}"));
                section.WithAccessory(new ThumbnailBuilder
                {
                    Media = new UnfurledMediaItemProperties
                        { Url = after.GetDisplayAvatarUrl() ?? after.GetDefaultAvatarUrl() }
                });
            })
            .WithSeparator();

        if (before.Username != after.Username)
            container.WithTextDisplay(
                new TextDisplayBuilder($"**Username:** `{before.Username}` → `{after.Username}`"));

        if (before.GetDisplayAvatarUrl() != after.GetDisplayAvatarUrl())
            container.WithMediaGallery(new List<string>
            {
                before.GetDisplayAvatarUrl() ?? before.GetDefaultAvatarUrl(),
                after.GetDisplayAvatarUrl() ?? after.GetDefaultAvatarUrl()
            });

        container
            .WithSeparator()
            .WithTextDisplay(new TextDisplayBuilder(
                $"User ID: {after.Id} | {TimestampTag.FromDateTimeOffset(DateTimeOffset.UtcNow)}"));

        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    private static MessageComponent BuildVoiceStateUpdateComponent(SocketGuildUser member, string actionDescription)
    {
        var container = new ContainerBuilder()
            .WithAccentColor(Color.DarkGreen)
            .WithSection(section =>
            {
                section.AddComponent(new TextDisplayBuilder("# Voice State Update"));
                section.AddComponent(new TextDisplayBuilder(member.Mention));
                section.WithAccessory(new ThumbnailBuilder
                {
                    Media = new UnfurledMediaItemProperties
                        { Url = member.GetDisplayAvatarUrl() ?? member.GetDefaultAvatarUrl() }
                });
            })
            .WithSeparator()
            .WithTextDisplay(new TextDisplayBuilder(actionDescription))
            .WithSeparator()
            .WithTextDisplay(new TextDisplayBuilder(
                $"User ID: {member.Id} | {TimestampTag.FromDateTimeOffset(DateTimeOffset.UtcNow)}"));

        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    private static MessageComponent BuildGuildEventComponent(SocketGuildUser user, string title, Color color)
    {
        var container = new ContainerBuilder()
            .WithAccentColor(color)
            .WithSection(section =>
            {
                section.AddComponent(new TextDisplayBuilder($"# {user.Username} {title}"));
                section.AddComponent(new TextDisplayBuilder(user.Mention));
                section.WithAccessory(new ThumbnailBuilder
                {
                    Media = new UnfurledMediaItemProperties
                        { Url = user.GetDisplayAvatarUrl() ?? user.GetDefaultAvatarUrl() }
                });
            });

        if (title.Equals("Joined", StringComparison.OrdinalIgnoreCase))
            container.WithTextDisplay(new TextDisplayBuilder(
                $"**Account Created:** {TimestampTag.FromDateTimeOffset(user.CreatedAt, TimestampTagStyles.Relative)}"));

        container
            .WithSeparator()
            .WithTextDisplay(new TextDisplayBuilder(
                $"User ID: {user.Id} | {TimestampTag.FromDateTimeOffset(user.JoinedAt ?? DateTimeOffset.UtcNow)}"));

        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    private static MessageComponent BuildBanEventComponent(SocketUser user, string banReason)
    {
        var container = new ContainerBuilder()
            .WithAccentColor(Color.DarkRed)
            .WithSection(section =>
            {
                section.AddComponent(new TextDisplayBuilder($"# {user.Username} Banned"));
                section.AddComponent(new TextDisplayBuilder(user.Mention));
                section.WithAccessory(new ThumbnailBuilder
                {
                    Media = new UnfurledMediaItemProperties
                        { Url = user.GetDisplayAvatarUrl() ?? user.GetDefaultAvatarUrl() }
                });
            })
            .WithSeparator()
            .WithTextDisplay(new TextDisplayBuilder($"**Reason:** {banReason}"))
            .WithSeparator()
            .WithTextDisplay(new TextDisplayBuilder(
                $"User ID: {user.Id} | {TimestampTag.FromDateTimeOffset(DateTimeOffset.UtcNow)}"));

        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    private static MessageComponent BuildUnbanEventComponent(SocketUser user)
    {
        var container = new ContainerBuilder()
            .WithAccentColor(Color.DarkGreen)
            .WithSection(section =>
            {
                section.AddComponent(new TextDisplayBuilder($"# {user.Username} Unbanned"));
                section.AddComponent(new TextDisplayBuilder(user.Mention));
                section.WithAccessory(new ThumbnailBuilder
                {
                    Media = new UnfurledMediaItemProperties
                        { Url = user.GetDisplayAvatarUrl() ?? user.GetDefaultAvatarUrl() }
                });
            })
            .WithSeparator()
            .WithTextDisplay(new TextDisplayBuilder(
                $"User ID: {user.Id} | {TimestampTag.FromDateTimeOffset(DateTimeOffset.UtcNow)}"));

        return new ComponentBuilderV2().WithContainer(container).Build();
    }


    // --- Event Handlers ---

    private async Task HandleMessageUpdatedAsync(Cacheable<IMessage, ulong> beforeCache, SocketMessage after,
        ISocketMessageChannel channel)
    {
        if (after.Author.IsBot ||
            (_config.Client.OwnerId.HasValue && after.Author.Id == _config.Client.OwnerId.Value)) return;
        if (channel is not SocketGuildChannel guildChannel) return;

        var loggingConfig = GetLoggingGuildConfig(guildChannel.Guild.Id);
        if (loggingConfig == null) return;

        var before = await beforeCache.GetOrDownloadAsync().ConfigureAwait(false);
        if (before == null || before.Content == after.Content) return;

        var webhookClient = await _webhookService.GetOrCreateWebhookClientAsync(loggingConfig.ChannelId)
            .ConfigureAwait(false);
        if (webhookClient == null) return;

        var author = after.Author;
        var components = BuildMessageUpdatedComponent(before, after, guildChannel);

        try
        {
            var msgId = await webhookClient.SendMessageAsync(
                components: components,
                username: author is SocketGuildUser sgu ? sgu.DisplayName : author.Username,
                avatarUrl: author.GetDisplayAvatarUrl() ?? author.GetDefaultAvatarUrl(),
                flags: MessageFlags.ComponentsV2
            ).ConfigureAwait(false);
            _logger.LogInformation("[MESSAGE EDIT] @{User} in #{Channel}", author.Username, guildChannel.Name);
            _ = Task.Delay(DeleteDelay)
                .ContinueWith(_ => webhookClient.DeleteMessageAsync(msgId).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send message edit log via webhook for User {UserId} in Channel {ChannelId}.", author.Id,
                guildChannel.Id);
        }
    }

    private async Task HandleMessageDeletedAsync(Cacheable<IMessage, ulong> messageCache,
        Cacheable<IMessageChannel, ulong> channelCache)
    {
        var channel = channelCache.HasValue
            ? channelCache.Value
            : await channelCache.GetOrDownloadAsync().ConfigureAwait(false);
        if (channel is not SocketGuildChannel guildChannel) return;

        var loggingConfig = GetLoggingGuildConfig(guildChannel.Guild.Id);
        if (loggingConfig == null) return;

        var message = await messageCache.GetOrDownloadAsync().ConfigureAwait(false);
        if (message == null || message.Author.IsBot ||
            (_config.Client.OwnerId.HasValue && message.Author.Id == _config.Client.OwnerId.Value)) return;

        var webhookClient = await _webhookService.GetOrCreateWebhookClientAsync(loggingConfig.ChannelId)
            .ConfigureAwait(false);
        if (webhookClient == null) return;

        var author = message.Author;
        var components = BuildMessageDeletedComponent(message, guildChannel);

        try
        {
            var msgId = await webhookClient.SendMessageAsync(
                components: components,
                username: author is SocketGuildUser sgu ? sgu.DisplayName : author.Username,
                avatarUrl: author.GetDisplayAvatarUrl() ?? author.GetDefaultAvatarUrl(),
                flags: MessageFlags.ComponentsV2
            ).ConfigureAwait(false);
            _logger.LogInformation("[MESSAGE DELETE] @{User} in #{Channel}\n\tMessage: {Content}", author.Username,
                guildChannel.Name, message.Content);
            _ = Task.Delay(DeleteDelay)
                .ContinueWith(_ => webhookClient.DeleteMessageAsync(msgId).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send message delete log via webhook for Message {MessageId} in Channel {ChannelId}.",
                message.Id, guildChannel.Id);
        }
    }

    private async Task HandleGuildMemberUpdatedAsync(Cacheable<SocketGuildUser, ulong> beforeCache,
        SocketGuildUser after)
    {
        if (after.IsBot || (_config.Client.OwnerId.HasValue && after.Id == _config.Client.OwnerId.Value)) return;

        var before = beforeCache.HasValue ? beforeCache.Value : null;
        if (before == null || before.DisplayName == after.DisplayName) return;

        var loggingConfig = GetLoggingGuildConfig(after.Guild.Id);
        if (loggingConfig == null) return;

        var webhookClient = await _webhookService.GetOrCreateWebhookClientAsync(loggingConfig.ChannelId)
            .ConfigureAwait(false);
        if (webhookClient == null) return;

        var components = BuildNicknameChangeComponent(before, after);

        try
        {
            var msgId = await webhookClient.SendMessageAsync(
                components: components,
                username: after.DisplayName,
                avatarUrl: after.GetDisplayAvatarUrl() ?? after.GetDefaultAvatarUrl(),
                flags: MessageFlags.ComponentsV2
            ).ConfigureAwait(false);
            _logger.LogInformation("[UPDATE] Nickname {GuildName}: @{OldName} -> @{NewName}", after.Guild.Name,
                before.DisplayName, after.DisplayName);
            _ = Task.Delay(DeleteDelay)
                .ContinueWith(_ => webhookClient.DeleteMessageAsync(msgId).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send nickname update log via webhook for User {UserId} in Guild {GuildId}.",
                after.Id, after.Guild.Id);
        }
    }

    private async Task HandleUserUpdatedAsync(SocketUser before, SocketUser after)
    {
        if (after.IsBot || (_config.Client.OwnerId.HasValue && after.Id == _config.Client.OwnerId.Value)) return;
        if (before.Username == after.Username &&
            before.GetDisplayAvatarUrl() == after.GetDisplayAvatarUrl()) return;

        foreach (var guild in _client.Guilds)
        {
            var loggingConfig = GetLoggingGuildConfig(guild.Id);
            if (loggingConfig == null || guild.GetUser(after.Id) == null) continue;

            var webhookClient = await _webhookService.GetOrCreateWebhookClientAsync(loggingConfig.ChannelId)
                .ConfigureAwait(false);
            if (webhookClient == null) continue;

            var components = BuildUserProfileUpdateComponent(before, after);

            try
            {
                var msgId = await webhookClient.SendMessageAsync(
                    components: components,
                    username: _client.CurrentUser.GlobalName,
                    avatarUrl: _client.CurrentUser.GetDisplayAvatarUrl() ?? _client.CurrentUser.GetDefaultAvatarUrl(),
                    flags: MessageFlags.ComponentsV2
                ).ConfigureAwait(false);
                _logger.LogInformation("[UPDATE] User Profile {GuildName}: @{BeforeUser} -> @{AfterUser}", guild.Name,
                    before, after);
                _ = Task.Delay(DeleteDelay)
                    .ContinueWith(_ => webhookClient.DeleteMessageAsync(msgId).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to send user profile update log via webhook for User {UserId} in Guild {GuildId}.",
                    after.Id, guild.Id);
            }
        }
    }

    private async Task HandlePresenceUpdatedAsync(SocketUser user, SocketPresence before, SocketPresence after)
    {
        if (user.IsBot || user is not SocketGuildUser guildUser) return;

        var loggingConfig = GetLoggingGuildConfig(guildUser.Guild.Id);
        if (loggingConfig is not { LogPresenceUpdates: true }) return;

        var bClients = ActivityUtils.GetClients(before);
        var aClients = ActivityUtils.GetClients(after);
        var bStatus = before.Status.ToString().ToLowerInvariant();
        var aStatus = after.Status.ToString().ToLowerInvariant();

        if (bStatus == aStatus &&
            bClients.SetEquals(aClients) &&
            before.Activities.SequenceEqual(after.Activities, ActivityComparer.Instance)) return;


        var webhookClient = await _webhookService.GetOrCreateWebhookClientAsync(loggingConfig.ChannelId)
            .ConfigureAwait(false);
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
        foreach (var key in allActivityKeys.Where(key => key != "Spotify"))
        {
            bActivities.TryGetValue(key, out var bValue);
            aActivities.TryGetValue(key, out var aValue);

            if (bValue == aValue) continue;
            activityChanged = true;

            var changeDescription = "";
            if (key == "Custom Status")
            {
                switch (string.IsNullOrEmpty(bValue))
                {
                    case false when !string.IsNullOrEmpty(aValue):
                        changeDescription = $"Custom Status: `{bValue}` → `{aValue}`";
                        break;
                    case false:
                        changeDescription = $"Removed Custom Status: `{bValue}`";
                        break;
                    default:
                    {
                        if (!string.IsNullOrEmpty(aValue)) changeDescription = $"Set Custom Status: `{aValue}`";
                        break;
                    }
                }
            }
            else
            {
                if (string.IsNullOrEmpty(bValue)) changeDescription = $"Started {key}: {aValue}";
                else if (string.IsNullOrEmpty(aValue)) changeDescription = $"Stopped {key}: {bValue}";
                else changeDescription = $"{key}: `{bValue}` → `{aValue}`";
            }

            if (!string.IsNullOrEmpty(changeDescription)) logParts.Add(changeDescription);
        }

        if (statusSummary == null && !activityChanged) return;

        var messageContent = string.Join("\n", logParts).Trim();
        if (string.IsNullOrEmpty(messageContent)) return;

        try
        {
            var msgId = await webhookClient.SendMessageAsync(
                messageContent.Truncate(DiscordConfig.MaxMessageSize),
                username: guildUser.DisplayName,
                avatarUrl: user.GetDisplayAvatarUrl() ?? user.GetDefaultAvatarUrl(),
                allowedMentions: AllowedMentions.None,
                flags: MessageFlags.SuppressEmbeds
            ).ConfigureAwait(false);
            _ = Task.Delay(DeleteDelay)
                .ContinueWith(_ => webhookClient.DeleteMessageAsync(msgId).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send presence update log via webhook for User {UserId}.", user.Id);
        }
    }


    private async Task HandleVoiceStateUpdatedAsync(SocketUser user, SocketVoiceState before, SocketVoiceState after)
    {
        if (user.IsBot || (_config.Client.OwnerId.HasValue && user.Id == _config.Client.OwnerId.Value) ||
            user is not SocketGuildUser member) return;

        if (before.VoiceChannel?.Id == after.VoiceChannel?.Id &&
            before.IsMuted == after.IsMuted && before.IsDeafened == after.IsDeafened &&
            before.IsSelfMuted == after.IsSelfMuted && before.IsSelfDeafened == after.IsSelfDeafened &&
            before.IsStreaming == after.IsStreaming && before.IsVideoing == after.IsVideoing &&
            before.IsSuppressed == after.IsSuppressed)
            return;

        var loggingConfig = GetLoggingGuildConfig(member.Guild.Id);
        if (loggingConfig == null) return;

        var webhookClient = await _webhookService.GetOrCreateWebhookClientAsync(loggingConfig.ChannelId)
            .ConfigureAwait(false);
        if (webhookClient == null) return;

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
                    $"🔄 Switched from {before.VoiceChannel.Mention} to {after.VoiceChannel.Mention}.");
        }

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

        if (actionDescription.Length == 0) return;

        var components = BuildVoiceStateUpdateComponent(member, actionDescription.ToString());

        try
        {
            var msgId = await webhookClient.SendMessageAsync(
                components: components,
                username: user.Username,
                avatarUrl: user.GetDisplayAvatarUrl() ?? user.GetDefaultAvatarUrl(),
                allowedMentions: AllowedMentions.None,
                flags: MessageFlags.ComponentsV2
            ).ConfigureAwait(false);
            _logger.LogInformation("[UPDATE] Voice {GuildName}: @{User}: {Action}", member.Guild.Name,
                member.Username, actionDescription.ToString().Replace("\n", " "));
            _ = Task.Delay(DeleteDelay)
                .ContinueWith(_ => webhookClient.DeleteMessageAsync(msgId).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send voice state update log via webhook for User {UserId}.", user.Id);
        }
    }

    private async Task HandleUserLeftAsync(SocketGuild guild, SocketUser user)
    {
        if (user.IsBot) return;

        var loggingConfig = GetLoggingGuildConfig(guild.Id);
        if (loggingConfig == null) return;

        var webhookClient = await _webhookService.GetOrCreateWebhookClientAsync(loggingConfig.ChannelId)
            .ConfigureAwait(false);
        if (webhookClient == null) return;

        var components = BuildGuildEventComponent(guild.GetUser(user.Id), "Left", Color.DarkGrey);

        try
        {
            var msgId = await webhookClient.SendMessageAsync(
                components: components,
                username: _client.CurrentUser.GlobalName,
                avatarUrl: _client.CurrentUser.GetDisplayAvatarUrl() ?? _client.CurrentUser.GetDefaultAvatarUrl(),
                flags: MessageFlags.ComponentsV2
            ).ConfigureAwait(false);
            _logger.LogInformation("[GUILD] Leave @{User}: {GuildName}", user.Username, guild.Name);
            _ = Task.Delay(DeleteDelay)
                .ContinueWith(_ => webhookClient.DeleteMessageAsync(msgId).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send user left log via webhook for User {UserId} in Guild {GuildId}.",
                user.Id, guild.Id);
        }
    }

    private async Task HandleUserJoinedAsync(SocketGuildUser member)
    {
        if (member.IsBot) return;

        var loggingConfig = GetLoggingGuildConfig(member.Guild.Id);
        if (loggingConfig == null) return;

        var webhookClient = await _webhookService.GetOrCreateWebhookClientAsync(loggingConfig.ChannelId)
            .ConfigureAwait(false);
        if (webhookClient == null) return;

        var components = BuildGuildEventComponent(member, "Joined", Color.Green);

        try
        {
            var msgId = await webhookClient.SendMessageAsync(
                components: components,
                username: _client.CurrentUser.GlobalName,
                avatarUrl: _client.CurrentUser.GetDisplayAvatarUrl() ?? _client.CurrentUser.GetDefaultAvatarUrl(),
                flags: MessageFlags.ComponentsV2
            ).ConfigureAwait(false);
            _logger.LogInformation("[GUILD] Join @{User}: {GuildName}", member.Username, member.Guild.Name);
            _ = Task.Delay(DeleteDelay)
                .ContinueWith(_ => webhookClient.DeleteMessageAsync(msgId).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send user joined log via webhook for User {UserId} in Guild {GuildId}.",
                member.Id, member.Guild.Id);
        }
    }

    private async Task HandleUserBannedAsync(SocketUser user, SocketGuild guild)
    {
        if (user.IsBot) return;

        var loggingConfig = GetLoggingGuildConfig(guild.Id);
        if (loggingConfig == null) return;

        var webhookClient = await _webhookService.GetOrCreateWebhookClientAsync(loggingConfig.ChannelId)
            .ConfigureAwait(false);
        if (webhookClient == null) return;

        var banReason = "Not specified";
        try
        {
            var ban = await guild.GetBanAsync(user).ConfigureAwait(false);
            if (ban != null && !string.IsNullOrWhiteSpace(ban.Reason)) banReason = ban.Reason;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not fetch ban reason for user {UserId} in guild {GuildId}", user.Id,
                guild.Id);
        }

        var components = BuildBanEventComponent(user, banReason);

        try
        {
            var msgId = await webhookClient.SendMessageAsync(
                components: components,
                username: _client.CurrentUser.GlobalName,
                avatarUrl: _client.CurrentUser.GetDisplayAvatarUrl() ?? _client.CurrentUser.GetDefaultAvatarUrl(),
                flags: MessageFlags.ComponentsV2
            ).ConfigureAwait(false);
            _logger.LogInformation("[GUILD] Ban @{User}: {GuildName}. Reason: {Reason}", user.Username, guild.Name,
                banReason);
            _ = Task.Delay(DeleteDelay)
                .ContinueWith(_ => webhookClient.DeleteMessageAsync(msgId).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send user banned log via webhook for User {UserId} in Guild {GuildId}.",
                user.Id, guild.Id);
        }
    }

    private async Task HandleUserUnbannedAsync(SocketUser user, SocketGuild guild)
    {
        if (user.IsBot) return;

        var loggingConfig = GetLoggingGuildConfig(guild.Id);
        if (loggingConfig == null) return;

        var webhookClient = await _webhookService.GetOrCreateWebhookClientAsync(loggingConfig.ChannelId)
            .ConfigureAwait(false);
        if (webhookClient == null) return;

        var components = BuildUnbanEventComponent(user);

        try
        {
            var msgId = await webhookClient.SendMessageAsync(
                components: components,
                username: _client.CurrentUser.GlobalName,
                avatarUrl: _client.CurrentUser.GetDisplayAvatarUrl() ?? _client.CurrentUser.GetDefaultAvatarUrl(),
                flags: MessageFlags.ComponentsV2
            ).ConfigureAwait(false);
            _logger.LogInformation("[GUILD] Unban @{User}: {GuildName}", user.Username, guild.Name);
            _ = Task.Delay(DeleteDelay)
                .ContinueWith(_ => webhookClient.DeleteMessageAsync(msgId).ConfigureAwait(false));
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