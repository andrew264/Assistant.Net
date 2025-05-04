using System.Collections.Concurrent;
using System.Net;
using Discord;
using Discord.Interactions;
using Discord.Net;
using Discord.Rest;
using Discord.Webhook;
using Discord.WebSocket;
using GTranslate.Translators;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Translate;

public class TranslationModule(
    ILogger<TranslationModule> logger,
    IHttpClientFactory httpClientFactory,
    IMemoryCache memoryCache,
    DiscordSocketClient client)
    : InteractionModuleBase<SocketInteractionContext>
{
    private const string WebhookCachePrefix = "webhook:";
    private const string AssistantWebhookName = "Assistant";
    private static readonly TimeSpan WebhookCacheDuration = TimeSpan.FromHours(1);
    private readonly BingTranslator _translator = new(); // only one that seems to match the functionality ?
    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _webhookLocks = new();

    private async Task<DiscordWebhookClient?> GetOrCreateWebhookAsync(ulong channelId)
    {
        var cacheKey = $"{WebhookCachePrefix}{channelId}";

        if (memoryCache.TryGetValue(cacheKey, out DiscordWebhookClient? cachedClient) && cachedClient != null)
            return cachedClient;
        var channelLock = _webhookLocks.GetOrAdd(channelId, _ => new SemaphoreSlim(1, 1));
        await channelLock.WaitAsync();

        try
        {
            if (memoryCache.TryGetValue(cacheKey, out cachedClient) && cachedClient != null)
                return cachedClient;

            var channel = client.GetChannel(channelId);
            if (channel is not SocketTextChannel textChannel)
            {
                logger.LogWarning("Target channel {ChannelId} not found or is not a text channel for webhook.",
                    channelId);
                return null;
            }

            var botGuildUser = textChannel.Guild.CurrentUser;
            if (botGuildUser == null || !botGuildUser.GetPermissions(textChannel).ManageWebhooks)
            {
                logger.LogError(
                    "Bot lacks 'Manage Webhooks' permission in channel {ChannelId} ({ChannelName}) in Guild {GuildId}.",
                    channelId, textChannel.Name, textChannel.Guild.Id);
                return null;
            }

            RestWebhook? existingWebhook = null;
            try
            {
                var webhooks = await textChannel.GetWebhooksAsync();
                existingWebhook = webhooks.FirstOrDefault(w => w.Name == AssistantWebhookName);
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
            }

            if (existingWebhook != null)
            {
                logger.LogDebug("Found existing webhook '{WebhookName}' ({WebhookId}) in channel {ChannelId}.",
                    existingWebhook.Name, existingWebhook.Id, channelId);
                var webhookClient = new DiscordWebhookClient(existingWebhook);
                memoryCache.Set(cacheKey, webhookClient, WebhookCacheDuration);
                return webhookClient;
            }
            else
            {
                logger.LogInformation("Webhook '{WebhookName}' not found in channel {ChannelId}. Creating new one.",
                    AssistantWebhookName, channelId);
                try
                {
                    Image? avatarImage = null;
                    Stream? avatarStream = null;
                    try
                    {
                        var avatarUrl = client.CurrentUser.GetDisplayAvatarUrl() ??
                                        client.CurrentUser.GetDefaultAvatarUrl();
                        using var httpClient = httpClientFactory.CreateClient();
                        var response = await httpClient.GetAsync(avatarUrl);
                        if (response.IsSuccessStatusCode)
                        {
                            avatarStream = await response.Content.ReadAsStreamAsync();
                            avatarImage = new Image(avatarStream);
                        }
                        else
                        {
                            logger.LogWarning(
                                "Failed to download bot avatar (Status: {StatusCode}) for webhook creation. Using default.",
                                response.StatusCode);
                        }
                    }
                    catch (Exception avatarEx)
                    {
                        logger.LogWarning(avatarEx,
                            "Error downloading bot avatar for webhook creation. Using default.");
                        if (avatarStream != null)
                            await avatarStream.DisposeAsync();
                        avatarStream = null;
                        avatarImage = null;
                    }

                    var newWebhook = await textChannel.CreateWebhookAsync(AssistantWebhookName, avatarImage?.Stream);

                    if (avatarStream != null)
                        await avatarStream.DisposeAsync();

                    logger.LogInformation("Created webhook '{WebhookName}' ({WebhookId}) in channel {ChannelId}.",
                        newWebhook.Name, newWebhook.Id, channelId);
                    var webhookClient = new DiscordWebhookClient(newWebhook);
                    memoryCache.Set(cacheKey, webhookClient, WebhookCacheDuration);
                    return webhookClient;
                }
                catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
                {
                    logger.LogError(ex, "Forbidden to create webhook in channel {ChannelId} ({ChannelName}).",
                        channelId, textChannel.Name);
                    return null;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to create webhook in channel {ChannelId} ({ChannelName}).", channelId,
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

    // --- Helper: Core Translation Logic ---
    private async Task<(string? Text, string? SourceLang)> TranslateTextAsync(string text, string targetLanguageCode,
        bool transliterate)
    {
        if (string.IsNullOrWhiteSpace(text)) return (null, null);

        try
        {
            if (transliterate)
            {
                var result = await _translator.TransliterateAsync(text, targetLanguageCode);
                return string.IsNullOrWhiteSpace(result.Transliteration)
                    ? (null, result.SourceLanguage?.Name)
                    : (result.Transliteration, result.SourceLanguage?.Name);
            }
            else
            {
                var result = await _translator.TranslateAsync(text, targetLanguageCode);
                return (result.Translation, result.SourceLanguage?.Name);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GTranslate error translating text to {TargetLang}. Text: {TextSnippet}",
                targetLanguageCode, text.Length > 50 ? text[..50] + "..." : text);
            return (null, null);
        }
    }

    // --- Message Command: Translate to English ---
    [MessageCommand("Translate to English")]
    public async Task TranslateMessageToEnglishAsync(IMessage message)
    {
        await DeferAsync(true);

        if (string.IsNullOrWhiteSpace(message.Content))
        {
            await FollowupAsync("The selected message has no text content to translate.", ephemeral: true);
            return;
        }

        var (translatedText, sourceLang) =
            await TranslateTextAsync(message.Content, Language.English.ToLanguageCode(), false);

        if (translatedText == null)
        {
            await FollowupAsync("Sorry, I couldn't translate that message.", ephemeral: true);
            return;
        }

        await FollowupAsync(
            $"**Original ({sourceLang}):**\n> {message.Content}\n\n**Translation (English):**\n> {translatedText}",
            ephemeral: true);
    }

    // --- Slash Command: Translate Text ---
    [SlashCommand("translate", "Translate the provided text to a specified language.")]
    public async Task TranslateSlashCommandAsync(
        [Summary("text", "The text to translate.")]
        string text,
        [Summary("language", "The language to translate to.")]
        Language language = Language.English,
        [Summary("transliterate", "Show pronunciation/transliteration instead of translation (if available).")]
        bool transliterate = false)
    {
        await DeferAsync(true);

        if (string.IsNullOrWhiteSpace(text))
        {
            await FollowupAsync("Please provide text to translate.", ephemeral: true);
            return;
        }

        var targetLangCode = language.ToLanguageCode();
        var (translatedText, _) = await TranslateTextAsync(text, targetLangCode, transliterate);

        if (translatedText == null)
        {
            await FollowupAsync("Sorry, translation failed. Please check the text or try again later.",
                ephemeral: true);
            return;
        }

        var webhookClient = await GetOrCreateWebhookAsync(Context.Channel.Id);
        if (webhookClient != null)
        {
            try
            {
                const int maxMessageLength = DiscordConfig.MaxMessageSize - 3;
                await webhookClient.SendMessageAsync(
                    translatedText.Length > DiscordConfig.MaxMessageSize
                        ? translatedText[..maxMessageLength] + "..."
                        : translatedText,
                    username: Context.User.GlobalName ?? Context.User.Username,
                    avatarUrl: Context.User.GetDisplayAvatarUrl() ?? Context.User.GetDefaultAvatarUrl(),
                    allowedMentions: AllowedMentions.None,
                    flags: MessageFlags.SuppressEmbeds
                );

                await FollowupAsync("Translation sent!", ephemeral: true);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send translation via webhook for Channel {ChannelId}.",
                    Context.Channel.Id);
                await FollowupAsync(translatedText, ephemeral: true);
            }
        }
        else
        {
            logger.LogWarning("Could not get webhook for Channel {ChannelId}, sending translation ephemerally.",
                Context.Channel.Id);
            await FollowupAsync(translatedText, ephemeral: true);
        }
    }
}