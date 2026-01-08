using Assistant.Net.Data.Enums;
using Assistant.Net.Services.Core;
using Assistant.Net.Services.Features;
using Assistant.Net.Utilities.Ui;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Services.Logging;

public class MessageLogger
{
    private readonly ILogger<MessageLogger> _logger;
    private readonly LoggingConfigService _loggingConfigService;
    private readonly WebhookService _webhookService;

    public MessageLogger(
        DiscordSocketClient client,
        ILogger<MessageLogger> logger,
        WebhookService webhookService,
        LoggingConfigService loggingConfigService)
    {
        _logger = logger;
        _webhookService = webhookService;
        _loggingConfigService = loggingConfigService;

        client.MessageUpdated += HandleMessageUpdatedAsync;
        client.MessageDeleted += HandleMessageDeletedAsync;
        client.MessagesBulkDeleted += HandleMessagesBulkDeletedAsync;

        _logger.LogInformation("MessageLogger initialized.");
    }

    private async Task HandleMessageUpdatedAsync(Cacheable<IMessage, ulong> beforeCache, SocketMessage after,
        ISocketMessageChannel channel)
    {
        if (after.Author.IsBot) return;
        if (channel is not SocketGuildChannel guildChannel) return;

        var logConfig = await _loggingConfigService.GetLogConfigAsync(guildChannel.Guild.Id, LogType.Message)
            .ConfigureAwait(false);

        if (!logConfig.IsEnabled || logConfig.ChannelId == null) return;

        var before = await beforeCache.GetOrDownloadAsync().ConfigureAwait(false);
        if (before == null || before.Content == after.Content) return;

        var webhookClient = await _webhookService.GetOrCreateWebhookClientAsync((ulong)logConfig.ChannelId.Value)
            .ConfigureAwait(false);
        if (webhookClient == null) return;

        var author = after.Author;
        var components = LogUiBuilder.BuildMessageUpdatedComponent(before, after, guildChannel);

        try
        {
            var msgId = await webhookClient.SendMessageAsync(
                components: components,
                username: author is SocketGuildUser sgu ? sgu.DisplayName : author.Username,
                avatarUrl: author.GetDisplayAvatarUrl() ?? author.GetDefaultAvatarUrl(),
                flags: MessageFlags.ComponentsV2
            ).ConfigureAwait(false);
            _logger.LogInformation("[MESSAGE EDIT] @{User} in #{Channel}", author.Username, guildChannel.Name);

            if (logConfig.DeleteDelayMs > 0)
                _ = Task.Delay(logConfig.DeleteDelayMs)
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

        var logConfig = await _loggingConfigService.GetLogConfigAsync(guildChannel.Guild.Id, LogType.Message)
            .ConfigureAwait(false);

        if (!logConfig.IsEnabled || logConfig.ChannelId == null) return;

        var message = await messageCache.GetOrDownloadAsync().ConfigureAwait(false);
        if (message == null || message.Author.IsBot) return;

        var webhookClient = await _webhookService.GetOrCreateWebhookClientAsync((ulong)logConfig.ChannelId.Value)
            .ConfigureAwait(false);
        if (webhookClient == null) return;

        var author = message.Author;
        var components = LogUiBuilder.BuildMessageDeletedComponent(message, guildChannel);

        try
        {
            var msgId = await webhookClient.SendMessageAsync(
                components: components,
                username: author is SocketGuildUser sgu ? sgu.DisplayName : author.Username,
                avatarUrl: author.GetDisplayAvatarUrl() ?? author.GetDefaultAvatarUrl(),
                flags: MessageFlags.ComponentsV2
            ).ConfigureAwait(false);
            _logger.LogInformation("[MESSAGE DELETE] @{User} in #{Channel}", author.Username, guildChannel.Name);

            if (logConfig.DeleteDelayMs > 0)
                _ = Task.Delay(logConfig.DeleteDelayMs)
                    .ContinueWith(_ => webhookClient.DeleteMessageAsync(msgId).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send message delete log via webhook for Message {MessageId} in Channel {ChannelId}.",
                message.Id, guildChannel.Id);
        }
    }

    private Task HandleMessagesBulkDeletedAsync(IReadOnlyCollection<Cacheable<IMessage, ulong>> msgs,
        Cacheable<IMessageChannel, ulong> chan) =>
        Task.CompletedTask;
}