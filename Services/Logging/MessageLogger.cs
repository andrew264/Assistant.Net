using Assistant.Net.Configuration;
using Assistant.Net.Services.Core;
using Assistant.Net.Utilities.Ui;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Services.Logging;

public class MessageLogger
{
    private const int DeleteDelay = 24 * 60 * 60 * 1000; // one day
    private readonly Config _config;
    private readonly ILogger<MessageLogger> _logger;
    private readonly WebhookService _webhookService;

    public MessageLogger(
        DiscordSocketClient client,
        Config config,
        ILogger<MessageLogger> logger,
        WebhookService webhookService)
    {
        _config = config;
        _logger = logger;
        _webhookService = webhookService;

        client.MessageUpdated += HandleMessageUpdatedAsync;
        client.MessageDeleted += HandleMessageDeletedAsync;
        client.MessagesBulkDeleted += HandleMessagesBulkDeletedAsync;

        _logger.LogInformation("MessageLogger initialized.");
    }

    private LoggingGuildConfig? GetLoggingGuildConfig(ulong guildId)
    {
        return _config.LoggingGuilds?.FirstOrDefault(kvp => kvp.Value.GuildId == guildId).Value;
    }

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
        var components = LogUiBuilder.BuildMessageDeletedComponent(message, guildChannel);

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

    private Task HandleMessagesBulkDeletedAsync(IReadOnlyCollection<Cacheable<IMessage, ulong>> msgs,
        Cacheable<IMessageChannel, ulong> chan) =>
        Task.CompletedTask;
}