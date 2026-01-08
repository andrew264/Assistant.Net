using Assistant.Net.Data.Enums;
using Assistant.Net.Services.Core;
using Assistant.Net.Services.Features;
using Assistant.Net.Utilities;
using Assistant.Net.Utilities.Ui;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Services.Logging;

public class UserLogger
{
    private readonly DiscordSocketClient _client;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<UserLogger> _logger;
    private readonly LoggingConfigService _loggingConfigService;
    private readonly WebhookService _webhookService;

    public UserLogger(
        DiscordSocketClient client,
        ILogger<UserLogger> logger,
        WebhookService webhookService,
        IHttpClientFactory httpClientFactory,
        LoggingConfigService loggingConfigService)
    {
        _client = client;
        _logger = logger;
        _webhookService = webhookService;
        _httpClientFactory = httpClientFactory;
        _loggingConfigService = loggingConfigService;

        _client.GuildMemberUpdated += HandleGuildMemberUpdatedAsync;
        _client.UserUpdated += HandleUserUpdatedAsync;
        _client.UserJoined += HandleUserJoinedAsync;
        _client.UserLeft += HandleUserLeftAsync;
        _client.UserBanned += HandleUserBannedAsync;
        _client.UserUnbanned += HandleUserUnbannedAsync;

        _logger.LogInformation("UserLogger initialized.");
    }

    private async Task HandleGuildMemberUpdatedAsync(Cacheable<SocketGuildUser, ulong> beforeCache,
        SocketGuildUser after)
    {
        if (after.IsBot) return;

        var before = beforeCache.HasValue ? beforeCache.Value : null;
        if (before == null || before.DisplayName == after.DisplayName) return;

        var logConfig = await _loggingConfigService.GetLogConfigAsync(after.Guild.Id, LogType.User)
            .ConfigureAwait(false);

        if (!logConfig.IsEnabled || logConfig.ChannelId == null) return;

        var webhookClient = await _webhookService.GetOrCreateWebhookClientAsync((ulong)logConfig.ChannelId.Value)
            .ConfigureAwait(false);
        if (webhookClient == null) return;

        var components = LogUiBuilder.BuildNicknameChangeComponent(before, after);

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

            if (logConfig.DeleteDelayMs > 0)
                _ = Task.Delay(logConfig.DeleteDelayMs)
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
        if (after.IsBot) return;
        if (before.Username == after.Username &&
            before.GetDisplayAvatarUrl() == after.GetDisplayAvatarUrl()) return;

        foreach (var guild in _client.Guilds)
        {
            if (guild.GetUser(after.Id) == null) continue;

            var logConfig = await _loggingConfigService.GetLogConfigAsync(guild.Id, LogType.User).ConfigureAwait(false);
            if (!logConfig.IsEnabled || logConfig.ChannelId == null) continue;

            var webhookClient = await _webhookService.GetOrCreateWebhookClientAsync((ulong)logConfig.ChannelId.Value)
                .ConfigureAwait(false);
            if (webhookClient == null) continue;

            var (components, attachments) = await LogUiBuilder
                .BuildUserProfileUpdateComponentAsync(before, after, _httpClientFactory, _logger)
                .ConfigureAwait(false);

            try
            {
                var msgId = await webhookClient.SendFilesAsync(
                    attachments,
                    "",
                    username: _client.CurrentUser.GlobalName,
                    avatarUrl: _client.CurrentUser.GetDisplayAvatarUrl() ?? _client.CurrentUser.GetDefaultAvatarUrl(),
                    components: components,
                    flags: MessageFlags.ComponentsV2
                ).ConfigureAwait(false);
                _logger.LogInformation("[UPDATE] User Profile {GuildName}: @{BeforeUser} -> @{AfterUser}", guild.Name,
                    before, after);

                if (logConfig.DeleteDelayMs > 0)
                    _ = Task.Delay(logConfig.DeleteDelayMs)
                        .ContinueWith(_ => webhookClient.DeleteMessageAsync(msgId).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to send user profile update log via webhook for User {UserId} in Guild {GuildId}.",
                    after.Id, guild.Id);
            }
            finally
            {
                AttachmentUtils.DisposeFileAttachments(attachments);
            }
        }
    }

    private async Task HandleUserLeftAsync(SocketGuild guild, SocketUser user)
    {
        if (user.IsBot) return;

        var logConfig = await _loggingConfigService.GetLogConfigAsync(guild.Id, LogType.User).ConfigureAwait(false);
        if (!logConfig.IsEnabled || logConfig.ChannelId == null) return;

        var webhookClient = await _webhookService.GetOrCreateWebhookClientAsync((ulong)logConfig.ChannelId.Value)
            .ConfigureAwait(false);
        if (webhookClient == null) return;

        var components = LogUiBuilder.BuildGuildEventComponent(guild.GetUser(user.Id), "Left", Color.DarkGrey);

        try
        {
            var msgId = await webhookClient.SendMessageAsync(
                components: components,
                username: _client.CurrentUser.GlobalName,
                avatarUrl: _client.CurrentUser.GetDisplayAvatarUrl() ?? _client.CurrentUser.GetDefaultAvatarUrl(),
                flags: MessageFlags.ComponentsV2
            ).ConfigureAwait(false);
            _logger.LogInformation("[GUILD] Leave @{User}: {GuildName}", user.Username, guild.Name);

            if (logConfig.DeleteDelayMs > 0)
                _ = Task.Delay(logConfig.DeleteDelayMs)
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

        var logConfig = await _loggingConfigService.GetLogConfigAsync(member.Guild.Id, LogType.User)
            .ConfigureAwait(false);
        if (!logConfig.IsEnabled || logConfig.ChannelId == null) return;

        var webhookClient = await _webhookService.GetOrCreateWebhookClientAsync((ulong)logConfig.ChannelId.Value)
            .ConfigureAwait(false);
        if (webhookClient == null) return;

        var components = LogUiBuilder.BuildGuildEventComponent(member, "Joined", Color.Green);

        try
        {
            var msgId = await webhookClient.SendMessageAsync(
                components: components,
                username: _client.CurrentUser.GlobalName,
                avatarUrl: _client.CurrentUser.GetDisplayAvatarUrl() ?? _client.CurrentUser.GetDefaultAvatarUrl(),
                flags: MessageFlags.ComponentsV2
            ).ConfigureAwait(false);
            _logger.LogInformation("[GUILD] Join @{User}: {GuildName}", member.Username, member.Guild.Name);

            if (logConfig.DeleteDelayMs > 0)
                _ = Task.Delay(logConfig.DeleteDelayMs)
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

        var logConfig = await _loggingConfigService.GetLogConfigAsync(guild.Id, LogType.User).ConfigureAwait(false);
        if (!logConfig.IsEnabled || logConfig.ChannelId == null) return;

        var webhookClient = await _webhookService.GetOrCreateWebhookClientAsync((ulong)logConfig.ChannelId.Value)
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

        var components = LogUiBuilder.BuildBanEventComponent(user, banReason);

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

            if (logConfig.DeleteDelayMs > 0)
                _ = Task.Delay(logConfig.DeleteDelayMs)
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

        var logConfig = await _loggingConfigService.GetLogConfigAsync(guild.Id, LogType.User).ConfigureAwait(false);
        if (!logConfig.IsEnabled || logConfig.ChannelId == null) return;

        var webhookClient = await _webhookService.GetOrCreateWebhookClientAsync((ulong)logConfig.ChannelId.Value)
            .ConfigureAwait(false);
        if (webhookClient == null) return;

        var components = LogUiBuilder.BuildUnbanEventComponent(user);

        try
        {
            var msgId = await webhookClient.SendMessageAsync(
                components: components,
                username: _client.CurrentUser.GlobalName,
                avatarUrl: _client.CurrentUser.GetDisplayAvatarUrl() ?? _client.CurrentUser.GetDefaultAvatarUrl(),
                flags: MessageFlags.ComponentsV2
            ).ConfigureAwait(false);
            _logger.LogInformation("[GUILD] Unban @{User}: {GuildName}", user.Username, guild.Name);

            if (logConfig.DeleteDelayMs > 0)
                _ = Task.Delay(logConfig.DeleteDelayMs)
                    .ContinueWith(_ => webhookClient.DeleteMessageAsync(msgId).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send user unbanned log via webhook for User {UserId} in Guild {GuildId}.",
                user.Id, guild.Id);
        }
    }
}