using Assistant.Net.Services.Core;
using Assistant.Net.Utilities;
using Discord;
using Discord.Interactions;
using GTranslate.Translators;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Translate;

public class TranslationModule(
    ILogger<TranslationModule> logger,
    WebhookService webhookService
) : InteractionModuleBase<SocketInteractionContext>
{
    private readonly BingTranslator _translator = new();

    // --- Helper: Core Translation Logic ---
    private async Task<(string? Text, string? SourceLang)> TranslateTextAsync(string text, string targetLanguageCode,
        bool transliterate)
    {
        if (string.IsNullOrWhiteSpace(text)) return (null, null);

        try
        {
            if (transliterate)
            {
                var result = await _translator.TransliterateAsync(text, targetLanguageCode).ConfigureAwait(false);
                return string.IsNullOrWhiteSpace(result.Transliteration)
                    ? (null, result.SourceLanguage?.Name)
                    : (result.Transliteration, result.SourceLanguage?.Name);
            }
            else
            {
                var result = await _translator.TranslateAsync(text, targetLanguageCode).ConfigureAwait(false);
                return (result.Translation, result.SourceLanguage?.Name);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GTranslate error translating text to {TargetLang}. Text: {TextSnippet}",
                targetLanguageCode, text.Truncate(50));
            return (null, null);
        }
    }

    // --- Message Command: Translate to English ---
    [MessageCommand("Translate to English")]
    public async Task TranslateMessageToEnglishAsync(IMessage message)
    {
        await DeferAsync(true).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(message.Content))
        {
            await FollowupAsync("The selected message has no text content to translate.", ephemeral: true)
                .ConfigureAwait(false);
            return;
        }

        var (translatedText, sourceLang) =
            await TranslateTextAsync(message.Content, Language.English.ToLanguageCode(), false).ConfigureAwait(false);

        if (translatedText == null)
        {
            await FollowupAsync("Sorry, I couldn't translate that message.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await FollowupAsync(
            $"**Original ({sourceLang}):**\n> {message.Content}\n\n**Translation (English):**\n> {translatedText}",
            ephemeral: true).ConfigureAwait(false);
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
        await DeferAsync(true).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(text))
        {
            await FollowupAsync("Please provide text to translate.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var targetLangCode = language.ToLanguageCode();
        var (translatedText, _) = await TranslateTextAsync(text, targetLangCode, transliterate).ConfigureAwait(false);

        if (translatedText == null)
        {
            await FollowupAsync("Sorry, translation failed. Please check the text or try again later.",
                ephemeral: true).ConfigureAwait(false);
            return;
        }

        var webhookClient = await webhookService.GetOrCreateWebhookClientAsync(Context.Channel.Id,
                WebhookService.DefaultWebhookName,
                Context.Client.CurrentUser.GetDisplayAvatarUrl() ?? Context.Client.CurrentUser.GetDefaultAvatarUrl())
            .ConfigureAwait(false);

        if (webhookClient != null)
        {
            try
            {
                await webhookClient.SendMessageAsync(
                    translatedText.Truncate(DiscordConfig.MaxMessageSize),
                    username: Context.User.GlobalName ?? Context.User.Username,
                    avatarUrl: Context.User.GetDisplayAvatarUrl() ?? Context.User.GetDefaultAvatarUrl(),
                    allowedMentions: AllowedMentions.None,
                    flags: MessageFlags.SuppressEmbeds
                ).ConfigureAwait(false);

                await FollowupAsync("Translation sent!", ephemeral: true).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send translation via webhook for Channel {ChannelId}.",
                    Context.Channel.Id);
                await FollowupAsync($"Translation: {translatedText}", ephemeral: true).ConfigureAwait(false);
            }
        }
        else
        {
            logger.LogWarning("Could not get webhook for Channel {ChannelId}, sending translation ephemerally.",
                Context.Channel.Id);
            await FollowupAsync($"Translation: {translatedText}", ephemeral: true).ConfigureAwait(false);
        }
    }
}