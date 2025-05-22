using System.Collections.Concurrent;
using System.Net;
using Discord;
using Discord.Net;
using Discord.Rest;
using Discord.Webhook;
using Discord.WebSocket;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Services;

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

    /// <summary>
    ///     Gets an existing webhook client from cache or retrieves/creates it for the specified channel.
    /// </summary>
    /// <param name="channelId">The ID of the text channel.</param>
    /// <param name="webhookName">The desired name of the webhook.</param>
    /// <param name="defaultAvatarUrl">Optional URL for the webhook avatar if creating a new one. If null, tries bot's avatar.</param>
    /// <returns>A DiscordWebhookClient if successful, otherwise null.</returns>
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
            // Double-check cache after acquiring lock
            if (memoryCache.TryGetValue(cacheKey, out cachedClient) && cachedClient != null)
            {
                logger.LogTrace("Webhook client cache hit (after lock) for Channel {ChannelId}, Name {WebhookName}",
                    channelId, webhookName);
                return cachedClient;
            }

            logger.LogTrace("Webhook client cache miss for Channel {ChannelId}, Name {WebhookName}", channelId,
                webhookName);

            if (client.GetChannel(channelId) is not SocketTextChannel textChannel)
            {
                logger.LogWarning("Target channel {ChannelId} not found or is not a text channel for webhook.",
                    channelId);
                return null;
            }

            var botGuildUser = textChannel.Guild.CurrentUser;
            if (botGuildUser == null || !botGuildUser.GetPermissions(textChannel).ManageWebhooks)
            {
                logger.LogError(
                    "Bot lacks 'Manage Webhooks' permission in channel {ChannelId} ({ChannelName}) in Guild {GuildId} for webhook '{WebhookName}'.",
                    channelId, textChannel.Name, textChannel.Guild.Id, webhookName);
                return null;
            }

            RestWebhook? existingWebhook = null;
            try
            {
                var webhooks = await textChannel.GetWebhooksAsync().ConfigureAwait(false);
                // Try to find by name and creator (bot) to ensure it's one we manage
                existingWebhook =
                    webhooks.FirstOrDefault(w => w.Name == webhookName && w.Creator.Id == client.CurrentUser.Id);
                existingWebhook ??=
                    webhooks.FirstOrDefault(w =>
                        w.Name == webhookName); // Fallback to just name if not found by creator
            }
            catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
            {
                logger.LogError(ex, "Forbidden to get webhooks in channel {ChannelId} ({ChannelName}).", channelId,
                    textChannel.Name);
                return null;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to get webhooks for channel {ChannelId} ({ChannelName}).", channelId,
                    textChannel.Name);
                // Potentially proceed to create if this was transient, but might indicate deeper issues.
            }

            if (existingWebhook != null)
            {
                logger.LogDebug("Found existing webhook '{WebhookName}' ({WebhookId}) in channel {ChannelId}.",
                    existingWebhook.Name, existingWebhook.Id, channelId);
                var webhookClient = new DiscordWebhookClient(existingWebhook); // Uses ID and Token
                memoryCache.Set(cacheKey, webhookClient, WebhookCacheDuration);
                return webhookClient;
            }

            // Create new webhook
            logger.LogInformation("Webhook '{WebhookName}' not found in channel {ChannelId}. Creating new one.",
                webhookName, channelId);
            Image? avatarImage = null;
            Stream? avatarStream = null;
            try
            {
                var avatarUrlToFetch = defaultAvatarUrl ??
                                       client.CurrentUser.GetDisplayAvatarUrl() ??
                                       client.CurrentUser.GetDefaultAvatarUrl();
                if (!string.IsNullOrEmpty(avatarUrlToFetch))
                {
                    using var httpClient = httpClientFactory.CreateClient("WebhookAvatar");
                    var response = await httpClient.GetAsync(avatarUrlToFetch).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        avatarStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                        avatarImage = new Image(avatarStream);
                    }
                    else
                    {
                        logger.LogWarning(
                            "Failed to download avatar (Status: {StatusCode}) from {AvatarUrl} for webhook creation. Using default.",
                            response.StatusCode, avatarUrlToFetch);
                    }
                }
            }
            catch (Exception avatarEx)
            {
                logger.LogWarning(avatarEx, "Error downloading avatar for webhook creation. Using default.");
            }

            try
            {
                var newWebhook = await textChannel.CreateWebhookAsync(webhookName, avatarImage?.Stream)
                    .ConfigureAwait(false);
                logger.LogInformation("Created webhook '{WebhookName}' ({WebhookId}) in channel {ChannelId}.",
                    newWebhook.Name, newWebhook.Id, channelId);
                var webhookClient = new DiscordWebhookClient(newWebhook);
                memoryCache.Set(cacheKey, webhookClient, WebhookCacheDuration);
                return webhookClient;
            }
            catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
            {
                logger.LogError(ex,
                    "Forbidden to create webhook '{WebhookName}' in channel {ChannelId} ({ChannelName}).", webhookName,
                    channelId, textChannel.Name);
                return null;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create webhook '{WebhookName}' in channel {ChannelId} ({ChannelName}).",
                    webhookName, channelId, textChannel.Name);
                return null;
            }
            finally
            {
                avatarStream?.Dispose();
            }
        }
        finally
        {
            channelLock.Release();
        }
    }
}