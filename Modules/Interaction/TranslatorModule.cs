using Assistant.Net.Services;
using Discord;
using Discord.Interactions;
using Discord.Webhook;

namespace Assistant.Net.Modules.Interaction;
public class TranslatorModule : InteractionModuleBase<SocketInteractionContext>
{
    public required MicrosoftTranslatorService MicrosoftTranslator { get; set; }

    private static async Task<DiscordWebhookClient> GetWebhookClient(ITextChannel channel)
    {
        var webhooks = await channel.GetWebhooksAsync();
        var webhook = webhooks.FirstOrDefault(x => x.Name == "Assistant");

        if (webhook != null)
            return new DiscordWebhookClient(webhook);

        var newWebhook = await channel.CreateWebhookAsync("Assistant");
        return new DiscordWebhookClient(newWebhook);
    }

    [SlashCommand("translate", "Translate text to another language")]
    public async Task TranslateTextAsync(
        [Summary(description: "Enter the text to translate")] string text,
        [Summary(description: "Enter the language to translate to")] Languages to,
        [Summary(description: "Enter the language to translate from")] Languages? from = null)
    {

        if (Context.Channel is ITextChannel channel)
        {
            await RespondAsync("Translating...", ephemeral: true);
            var translation = await MicrosoftTranslator.TranslateAsync(text, to.ToString(), from?.ToString());
            var webhook = await GetWebhookClient(channel);
            await webhook.SendMessageAsync(
                text: translation,
                username: Context.User.GlobalName ?? Context.User.Username,
                avatarUrl: Context.User.GetAvatarUrl(size: 128)
            );
        }
        else
        {
            await DeferAsync();
            var translation = await MicrosoftTranslator.TranslateAsync(text, to.ToString(), from?.ToString());
            await ModifyOriginalResponseAsync(x => x.Content = translation);
        }
    }

    [MessageCommand("To English")]
    public async Task TranslateToEnglishAsync(IMessage message)
    {
        await DeferAsync();
        var translation = await MicrosoftTranslator.TranslateAsync(message.Content, Languages.en.ToString());
        await ModifyOriginalResponseAsync(x => x.Content = translation);
    }

    public enum Languages
    {
        [ChoiceDisplay("English")] en,
        [ChoiceDisplay("Japanese")] ja,
        [ChoiceDisplay("Tamil")] ta,
        [ChoiceDisplay("Spanish")] es,
        [ChoiceDisplay("French")] fr,
        [ChoiceDisplay("Hindi")] hi,
        [ChoiceDisplay("Russian")] ru,
        [ChoiceDisplay("German")] de,
        [ChoiceDisplay("Malayalam")] ml,
        [ChoiceDisplay("Bengali")] bn,
    }
}
