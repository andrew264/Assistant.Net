using Assistant.Net.Services;
using Discord.Interactions;

namespace Assistant.Net.Modules.Interaction;
public class UrbanDictionaryModule : InteractionModuleBase<SocketInteractionContext>
{
    public required InteractionService Commands { get; set; }

    public required UrbanDictionaryService UrbanDictionary { get; set; }

    [SlashCommand("define", "Find wth does words mean from UrbanDictionary")]
    public async Task DefineWordAsync([Summary(description: "Enter a word")] string word = "")
    {
        await DeferAsync();
        var definition = await UrbanDictionary.GetDefinitionAsync(word);

        await FollowupAsync(text: definition[..Math.Min(2000, definition.Length)]);

        for (int i = 2000; i < definition.Length; i += 2000)
            await Context.Channel.SendMessageAsync(text: definition.Substring(i, Math.Min(2000, definition.Length - i)));
    }
}
