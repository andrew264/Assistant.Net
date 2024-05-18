using Assistant.Net.Services;
using Discord;
using Discord.Interactions;
using Discord.Webhook;

namespace Assistant.Net.Modules;

public class UtilityModule : InteractionModuleBase<SocketInteractionContext>
{
    public required InteractionService Commands { get; set; }

    public required UrbanDictionaryService UrbanDictionary { get; set; }

    public required MicrosoftTranslatorService MicrosoftTranslator { get; set; }


    [SlashCommand("ping", "Pings the bot and returns its latency.")]
    public async Task GreetUserAsync()
        => await RespondAsync(text: $"Pong! {Context.Client.Latency}ms", ephemeral: true);

    [SlashCommand("define", "Find wth does words mean from UrbanDictionary")]
    public async Task DefineWordAsync([Summary(description: "Enter a word")] string word = "")
    {
        await DeferAsync();
        var definition = await UrbanDictionary.GetDefinitionAsync(word);
        await FollowupAsync(text: definition.Substring(0, Math.Min(2000, definition.Length)));
        for (int i = 2000; i < definition.Length; i += 2000)
        {
            await Context.Channel.SendMessageAsync(text: definition.Substring(i, Math.Min(2000, definition.Length - i)));
        }
    }

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