using System.Collections.Concurrent;
using System.Net;
using Discord.Net;
using Discord.Rest;
using Discord.Webhook;
using Discord.WebSocket;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Services.Core;

public class WebhookService(
    DiscordSocketClient client,
    IHttpClientFactory httpClientFactory,
    IMemoryCache memoryCache,
    ILogger<WebhookService> logger)
{
    public const string DefaultWebhookName = "Assistant";
    private const string WebhookCachePrefix = "webhook_client:";
    private static readonly TimeSpan WebhookCacheDuration = TimeSpan.FromHours(2);

    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _channelLocks = new();

    public async Task<DiscordWebhookClient?> GetOrCreateWebhookClientAsync(
        ulong channelId,
        string webhookName = DefaultWebhookName,
        string? defaultAvatarUrl = null)
    {
        var cacheKey = $"{WebhookCachePrefix}{channelId}:{webhookName}";

        if (memoryCache.TryGetValue(cacheKey, out DiscordWebhookClient? cachedClient) && cachedClient != null)
        {
            logger.LogTrace("Webhook client cache hit for Channel {ChannelId}, Name {WebhookName}", channelId,
                webhookName);
            return cachedClient;
        }

        var channelLock = _channelLocks.GetOrAdd(channelId, _ => new SemaphoreSlim(1, 1));
        await channelLock.WaitAsync();
        try
        {
            if (memoryCache.TryGetValue(cacheKey, out cachedClient) && cachedClient != null)
                return cachedClient;

            return await ResolveAndCacheWebhookAsync(channelId, webhookName, defaultAvatarUrl, cacheKey);
        }
        finally
        {
            channelLock.Release();
        }
    }

    private async Task<DiscordWebhookClient?> ResolveAndCacheWebhookAsync(
        ulong channelId,
        string webhookName,
        string? defaultAvatarUrl,
        string cacheKey)
    {
        if (client.GetChannel(channelId) is not SocketTextChannel textChannel)
        {
            logger.LogWarning("Channel {ChannelId} not found or is not a text channel.", channelId);
            return null;
        }

        if (!textChannel.Guild.CurrentUser.GetPermissions(textChannel).ManageWebhooks)
        {
            logger.LogError(
                "Bot lacks 'Manage Webhooks' in channel {ChannelId} ({ChannelName}), Guild {GuildId}.",
                channelId, textChannel.Name, textChannel.Guild.Id);
            return null;
        }

        var existing = await FindExistingWebhookAsync(textChannel, webhookName);
        var webhook = existing ?? await CreateWebhookAsync(textChannel, webhookName, defaultAvatarUrl);

        if (webhook is null)
            return null;

        var webhookClient = new DiscordWebhookClient(webhook);
        var cacheOptions = new MemoryCacheEntryOptions()
            .SetSlidingExpiration(WebhookCacheDuration)
            .SetSize(1);
        memoryCache.Set(cacheKey, webhookClient, cacheOptions);
        return webhookClient;
    }

    private async Task<RestWebhook?> FindExistingWebhookAsync(SocketTextChannel channel, string webhookName)
    {
        try
        {
            var webhooks = await channel.GetWebhooksAsync().ConfigureAwait(false);
            var match = webhooks.FirstOrDefault(w => w.Name == webhookName && w.Creator.Id == client.CurrentUser.Id)
                        ?? webhooks.FirstOrDefault(w => w.Name == webhookName);

            if (match is not null)
                logger.LogDebug("Found existing webhook '{Name}' ({Id}) in channel {ChannelId}.", match.Name, match.Id,
                    channel.Id);

            return match;
        }
        catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
        {
            logger.LogError(ex, "Forbidden to list webhooks in channel {ChannelId} ({Name}).", channel.Id,
                channel.Name);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to list webhooks in channel {ChannelId} ({Name}).", channel.Id, channel.Name);
            return null;
        }
    }

    private async Task<RestWebhook?> CreateWebhookAsync(
        SocketTextChannel channel,
        string webhookName,
        string? defaultAvatarUrl)
    {
        logger.LogInformation("Creating webhook '{Name}' in channel {ChannelId}.", webhookName, channel.Id);

        await using var avatarStream = await TryFetchAvatarAsync(defaultAvatarUrl);

        try
        {
            var webhook = await channel.CreateWebhookAsync(webhookName, avatarStream).ConfigureAwait(false);
            logger.LogInformation("Created webhook '{Name}' ({Id}) in channel {ChannelId}.", webhook.Name, webhook.Id,
                channel.Id);
            return webhook;
        }
        catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
        {
            logger.LogError(ex, "Forbidden to create webhook '{Name}' in channel {ChannelId} ({ChannelName}).",
                webhookName, channel.Id, channel.Name);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create webhook '{Name}' in channel {ChannelId} ({ChannelName}).",
                webhookName, channel.Id, channel.Name);
            return null;
        }
    }

    private async Task<Stream?> TryFetchAvatarAsync(string? defaultAvatarUrl)
    {
        var url = defaultAvatarUrl
                  ?? client.CurrentUser.GetDisplayAvatarUrl()
                  ?? client.CurrentUser.GetDefaultAvatarUrl();

        if (string.IsNullOrEmpty(url))
            return null;

        try
        {
            using var httpClient = httpClientFactory.CreateClient("WebhookAvatar");
            var response = await httpClient.GetAsync(url).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Failed to download avatar (HTTP {Status}) from {Url}.", response.StatusCode, url);
                return null;
            }

            return await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error downloading avatar for webhook. Proceeding without.");
            return null;
        }
    }
}