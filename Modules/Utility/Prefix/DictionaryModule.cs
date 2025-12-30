using Assistant.Net.Services.ExternalApis;
using Assistant.Net.Utilities;
using Discord;
using Discord.Commands;

namespace Assistant.Net.Modules.Utility.Prefix;

public class DictionaryModule(UrbanDictionaryService urbanService)
    : ModuleBase<SocketCommandContext>
{
    [Command("define", RunMode = RunMode.Async)]
    [Alias("def", "dictionary", "dict")]
    [Summary("Define a word using Urban Dictionary.")]
    public async Task DefinePrefixAsync([Remainder] string? word = null)
    {
        var results = await urbanService.GetDefinitionsAsync(word).ConfigureAwait(false);

        if (results == null)
        {
            await ReplyAsync("Sorry, I couldn't fetch the definition due to an API error.",
                allowedMentions: AllowedMentions.None).ConfigureAwait(false);
            return;
        }

        if (results.Count == 0)
        {
            var message = string.IsNullOrEmpty(word)
                ? "Sorry, I couldn't find any random definitions right now."
                : $"No definition found for **{word.Trim()}**.";
            await ReplyAsync(message, allowedMentions: AllowedMentions.None).ConfigureAwait(false);
            return;
        }

        var responseComponent =
            DictionaryUtils.BuildDefinitionResponse(results, 0, Context.User.Id, word ?? "_RANDOM_");

        await ReplyAsync(components: responseComponent, flags: MessageFlags.ComponentsV2,
            allowedMentions: AllowedMentions.None).ConfigureAwait(false);
    }
}