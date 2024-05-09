using Assistant.Net.Services;
using Discord.Interactions;

namespace Assistant.Net.Modules;

public class UtilityModule : InteractionModuleBase<SocketInteractionContext>
{
    public InteractionService Commands { get; set; }

    public UrbanDictionaryService UrbanDictionary { get; set; }

    private readonly InteractionHandler _handler;

    public UtilityModule(InteractionHandler handler)
    {
        _handler = handler;
    }

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

}
