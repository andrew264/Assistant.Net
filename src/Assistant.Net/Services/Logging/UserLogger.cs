using Assistant.Net.Data.Enums;
using Assistant.Net.Services.Core;
using Assistant.Net.Services.Features;
using Assistant.Net.Utilities;
using Assistant.Net.Utilities.Ui;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Services.Logging;

public class UserLogger(
    DiscordSocketClient client,
    ILogger<UserLogger> logger,
    WebhookService webhookService,
    IHttpClientFactory httpClientFactory,
    LoggingConfigService loggingConfigService)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        client.GuildMemberUpdated += HandleGuildMemberUpdatedAsync;
        client.UserUpdated += HandleUserUpdatedAsync;
        client.UserJoined += HandleUserJoinedAsync;
        client.UserLeft += HandleUserLeftAsync;
        client.UserBanned += HandleUserBannedAsync;
        client.UserUnbanned += HandleUserUnbannedAsync;

        logger.LogInformation("UserLogger started.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        client.GuildMemberUpdated -= HandleGuildMemberUpdatedAsync;
        client.UserUpdated -= HandleUserUpdatedAsync;
        client.UserJoined -= HandleUserJoinedAsync;
        client.UserLeft -= HandleUserLeftAsync;
        client.UserBanned -= HandleUserBannedAsync;
        client.UserUnbanned -= HandleUserUnbannedAsync;

        logger.LogInformation("UserLogger stopped.");
        return Task.CompletedTask;
    }

    private Task HandleGuildMemberUpdatedAsync(Cacheable<SocketGuildUser, ulong> beforeCache,
        SocketGuildUser after)
    {
        return Task.Run(async () =>
        {
            if (after.IsBot) return;

            var before = beforeCache.HasValue ? beforeCache.Value : null;
            if (before == null || before.DisplayName == after.DisplayName) return;

            var logConfig = await loggingConfigService.GetLogConfigAsync(after.Guild.Id, LogType.User)
                .ConfigureAwait(false);

            if (!logConfig.IsEnabled || logConfig.ChannelId == null) return;

            var webhookClient = await webhookService.GetOrCreateWebhookClientAsync(logConfig.ChannelId.Value)
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
                logger.LogInformation("[UPDATE] Nickname {GuildName}: @{OldName} -> @{NewName}", after.Guild.Name,
                    before.DisplayName, after.DisplayName);

                if (logConfig.DeleteDelayMs > 0)
                    _ = Task.Delay(logConfig.DeleteDelayMs)
                        .ContinueWith(_ => webhookClient.DeleteMessageAsync(msgId).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Failed to send nickname update log via webhook for User {UserId} in Guild {GuildId}.",
                    after.Id, after.Guild.Id);
            }
        });
    }

    private Task HandleUserUpdatedAsync(SocketUser before, SocketUser after)
    {
        return Task.Run(async () =>
        {
            if (after.IsBot) return;
            if (before.Username == after.Username &&
                before.GetDisplayAvatarUrl() == after.GetDisplayAvatarUrl()) return;

            foreach (var guild in client.Guilds)
            {
                if (guild.GetUser(after.Id) == null) continue;

                var logConfig = await loggingConfigService.GetLogConfigAsync(guild.Id, LogType.User)
                    .ConfigureAwait(false);
                if (!logConfig.IsEnabled || logConfig.ChannelId == null) continue;

                var webhookClient = await webhookService
                    .GetOrCreateWebhookClientAsync(logConfig.ChannelId.Value)
                    .ConfigureAwait(false);
                if (webhookClient == null) continue;

                var (components, attachments) = await LogUiBuilder
                    .BuildUserProfileUpdateComponentAsync(before, after, httpClientFactory, logger)
                    .ConfigureAwait(false);

                try
                {
                    var msgId = await webhookClient.SendFilesAsync(
                        attachments,
                        "",
                        username: client.CurrentUser.GlobalName,
                        avatarUrl: client.CurrentUser.GetDisplayAvatarUrl() ??
                                   client.CurrentUser.GetDefaultAvatarUrl(),
                        components: components,
                        flags: MessageFlags.ComponentsV2
                    ).ConfigureAwait(false);
                    logger.LogInformation("[UPDATE] User Profile {GuildName}: @{BeforeUser} -> @{AfterUser}",
                        guild.Name,
                        before, after);

                    if (logConfig.DeleteDelayMs > 0)
                        _ = Task.Delay(logConfig.DeleteDelayMs)
                            .ContinueWith(_ => webhookClient.DeleteMessageAsync(msgId).ConfigureAwait(false));
                }
                catch (Exception ex)
                {
                    logger.LogError(ex,
                        "Failed to send user profile update log via webhook for User {UserId} in Guild {GuildId}.",
                        after.Id, guild.Id);
                }
                finally
                {
                    AttachmentUtils.DisposeFileAttachments(attachments);
                }
            }
        });
    }

    private Task HandleUserLeftAsync(SocketGuild guild, SocketUser user)
    {
        return Task.Run(async () =>
        {
            if (user.IsBot) return;

            var logConfig = await loggingConfigService.GetLogConfigAsync(guild.Id, LogType.User).ConfigureAwait(false);
            if (!logConfig.IsEnabled || logConfig.ChannelId == null) return;

            var webhookClient = await webhookService.GetOrCreateWebhookClientAsync(logConfig.ChannelId.Value)
                .ConfigureAwait(false);
            if (webhookClient == null) return;

            var components = LogUiBuilder.BuildGuildEventComponent(guild.GetUser(user.Id), "Left", Color.DarkGrey);

            try
            {
                var msgId = await webhookClient.SendMessageAsync(
                    components: components,
                    username: client.CurrentUser.GlobalName,
                    avatarUrl: client.CurrentUser.GetDisplayAvatarUrl() ?? client.CurrentUser.GetDefaultAvatarUrl(),
                    flags: MessageFlags.ComponentsV2
                ).ConfigureAwait(false);
                logger.LogInformation("[GUILD] Leave @{User}: {GuildName}", user.Username, guild.Name);

                if (logConfig.DeleteDelayMs > 0)
                    _ = Task.Delay(logConfig.DeleteDelayMs)
                        .ContinueWith(_ => webhookClient.DeleteMessageAsync(msgId).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send user left log via webhook for User {UserId} in Guild {GuildId}.",
                    user.Id, guild.Id);
            }
        });
    }

    private Task HandleUserJoinedAsync(SocketGuildUser member)
    {
        return Task.Run(async () =>
        {
            if (member.IsBot) return;

            var logConfig = await loggingConfigService.GetLogConfigAsync(member.Guild.Id, LogType.User)
                .ConfigureAwait(false);
            if (!logConfig.IsEnabled || logConfig.ChannelId == null) return;

            var webhookClient = await webhookService.GetOrCreateWebhookClientAsync(logConfig.ChannelId.Value)
                .ConfigureAwait(false);
            if (webhookClient == null) return;

            var components = LogUiBuilder.BuildGuildEventComponent(member, "Joined", Color.Green);

            try
            {
                var msgId = await webhookClient.SendMessageAsync(
                    components: components,
                    username: client.CurrentUser.GlobalName,
                    avatarUrl: client.CurrentUser.GetDisplayAvatarUrl() ?? client.CurrentUser.GetDefaultAvatarUrl(),
                    flags: MessageFlags.ComponentsV2
                ).ConfigureAwait(false);
                logger.LogInformation("[GUILD] Join @{User}: {GuildName}", member.Username, member.Guild.Name);

                if (logConfig.DeleteDelayMs > 0)
                    _ = Task.Delay(logConfig.DeleteDelayMs)
                        .ContinueWith(_ => webhookClient.DeleteMessageAsync(msgId).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send user joined log via webhook for User {UserId} in Guild {GuildId}.",
                    member.Id, member.Guild.Id);
            }
        });
    }

    private Task HandleUserBannedAsync(SocketUser user, SocketGuild guild)
    {
        return Task.Run(async () =>
        {
            if (user.IsBot) return;

            var logConfig = await loggingConfigService.GetLogConfigAsync(guild.Id, LogType.User).ConfigureAwait(false);
            if (!logConfig.IsEnabled || logConfig.ChannelId == null) return;

            var webhookClient = await webhookService.GetOrCreateWebhookClientAsync(logConfig.ChannelId.Value)
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
                logger.LogWarning(ex, "Could not fetch ban reason for user {UserId} in guild {GuildId}", user.Id,
                    guild.Id);
            }

            var components = LogUiBuilder.BuildBanEventComponent(user, banReason);

            try
            {
                var msgId = await webhookClient.SendMessageAsync(
                    components: components,
                    username: client.CurrentUser.GlobalName,
                    avatarUrl: client.CurrentUser.GetDisplayAvatarUrl() ?? client.CurrentUser.GetDefaultAvatarUrl(),
                    flags: MessageFlags.ComponentsV2
                ).ConfigureAwait(false);
                logger.LogInformation("[GUILD] Ban @{User}: {GuildName}. Reason: {Reason}", user.Username, guild.Name,
                    banReason);

                if (logConfig.DeleteDelayMs > 0)
                    _ = Task.Delay(logConfig.DeleteDelayMs)
                        .ContinueWith(_ => webhookClient.DeleteMessageAsync(msgId).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send user banned log via webhook for User {UserId} in Guild {GuildId}.",
                    user.Id, guild.Id);
            }
        });
    }

    private Task HandleUserUnbannedAsync(SocketUser user, SocketGuild guild)
    {
        return Task.Run(async () =>
        {
            if (user.IsBot) return;

            var logConfig = await loggingConfigService.GetLogConfigAsync(guild.Id, LogType.User).ConfigureAwait(false);
            if (!logConfig.IsEnabled || logConfig.ChannelId == null) return;

            var webhookClient = await webhookService.GetOrCreateWebhookClientAsync(logConfig.ChannelId.Value)
                .ConfigureAwait(false);
            if (webhookClient == null) return;

            var components = LogUiBuilder.BuildUnbanEventComponent(user);

            try
            {
                var msgId = await webhookClient.SendMessageAsync(
                    components: components,
                    username: client.CurrentUser.GlobalName,
                    avatarUrl: client.CurrentUser.GetDisplayAvatarUrl() ?? client.CurrentUser.GetDefaultAvatarUrl(),
                    flags: MessageFlags.ComponentsV2
                ).ConfigureAwait(false);
                logger.LogInformation("[GUILD] Unban @{User}: {GuildName}", user.Username, guild.Name);

                if (logConfig.DeleteDelayMs > 0)
                    _ = Task.Delay(logConfig.DeleteDelayMs)
                        .ContinueWith(_ => webhookClient.DeleteMessageAsync(msgId).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Failed to send user unbanned log via webhook for User {UserId} in Guild {GuildId}.",
                    user.Id, guild.Id);
            }
        });
    }
}