using System.Text;
using Assistant.Net.Models.UrbanDictionary;
using Discord;

namespace Assistant.Net.Utilities;

public static class DictionaryUtils
{
    public const string CustomIdPrefix = "assistant:ud_page";

    public static MessageComponent BuildDefinitionResponse(List<UrbanDictionaryEntry> results, int pageIndex,
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
        // Encode search term to safely include in custom ID
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