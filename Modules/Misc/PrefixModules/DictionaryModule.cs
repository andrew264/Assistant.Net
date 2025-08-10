using System.Text;
using Assistant.Net.Models.UrbanDictionary;
using Assistant.Net.Services.ExternalApis;
using Assistant.Net.Utilities;
using Discord;
using Discord.Commands;

namespace Assistant.Net.Modules.Misc.PrefixModules;

public class DictionaryModule(UrbanDictionaryService urbanService)
    : ModuleBase<SocketCommandContext>
{
    private const string CustomIdPrefix = "assistant:ud_page";

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

        var responseComponent = BuildDefinitionResponse(results, 0, Context.User.Id, word ?? "_RANDOM_");

        await ReplyAsync(components: responseComponent, flags: MessageFlags.ComponentsV2,
            allowedMentions: AllowedMentions.None).ConfigureAwait(false);
    }

    private static MessageComponent BuildDefinitionResponse(List<UrbanDictionaryEntry> results, int pageIndex,
        ulong requesterId, string searchTerm)
    {
        var totalPages = results.Count;
        var currentPage = Math.Clamp(pageIndex, 0, totalPages - 1);
        var entry = results[currentPage];

        var container = new ContainerBuilder()
            .WithSection(section =>
            {
                section.AddComponent(new TextDisplayBuilder($"# {entry.Word}"));
                section.AddComponent(new TextDisplayBuilder($"*by {entry.Author}*"));
                section.WithAccessory(new ButtonBuilder("View on UD", style: ButtonStyle.Link, url: entry.Permalink));
            })
            .WithSeparator();

        container.WithTextDisplay(new TextDisplayBuilder(entry.FormattedDefinition.Truncate(1024)));

        if (!string.IsNullOrWhiteSpace(entry.FormattedExample))
            container
                .WithSeparator()
                .WithTextDisplay(new TextDisplayBuilder(
                    $"*Example:*\n{entry.FormattedExample.Truncate(1024)}"));

        container
            .WithSeparator()
            .WithTextDisplay(new TextDisplayBuilder($"{entry.ThumbsUp} 👍  •  {entry.ThumbsDown} 👎"));

        if (totalPages <= 1) return new ComponentBuilderV2().WithContainer(container).Build();
        var encodedSearchTerm =
            Convert.ToBase64String(Encoding.UTF8.GetBytes(searchTerm)).Replace('+', '-').Replace('/', '_');

        var footerText = $"Definition {currentPage + 1} of {totalPages}";
        container.WithTextDisplay(new TextDisplayBuilder(footerText));

        container.WithActionRow(row =>
        {
            row.WithButton("Previous",
                $"{CustomIdPrefix}:{requesterId}:{encodedSearchTerm}:{currentPage}:prev",
                ButtonStyle.Secondary, disabled: currentPage == 0);
            row.WithButton("Next",
                $"{CustomIdPrefix}:{requesterId}:{encodedSearchTerm}:{currentPage}:next",
                ButtonStyle.Secondary, disabled: currentPage == totalPages - 1);
        });

        return new ComponentBuilderV2().WithContainer(container).Build();
    }
}