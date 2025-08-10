using System.Text;
using Assistant.Net.Models.UrbanDictionary;
using Assistant.Net.Services.ExternalApis;
using Assistant.Net.Utilities;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Misc.InteractionModules;

public class DictionaryInteractionModule(
    UrbanDictionaryService urbanService,
    ILogger<DictionaryInteractionModule> logger)
    : InteractionModuleBase<SocketInteractionContext>
{
    private const string CustomIdPrefix = "assistant:ud_page";

    [SlashCommand("define", "Define a word using Urban Dictionary.")]
    public async Task DefineSlashAsync(
        [Summary("word", "The word to define (optional, gets random if empty)")]
        string? word = null)
    {
        await DeferAsync().ConfigureAwait(false);

        var results = await urbanService.GetDefinitionsAsync(word).ConfigureAwait(false);

        if (results == null)
        {
            await FollowupAsync("Sorry, I couldn't fetch the definition due to an API error.", ephemeral: true)
                .ConfigureAwait(false);
            return;
        }

        if (results.Count == 0)
        {
            var message = string.IsNullOrEmpty(word)
                ? "Sorry, I couldn't find any random definitions right now."
                : $"No definition found for **{word.Trim()}**.";
            await FollowupAsync(message, ephemeral: true).ConfigureAwait(false);
            return;
        }

        var responseComponent =
            BuildDefinitionResponse(results, 0, Context.User.Id, word ?? "_RANDOM_");

        await FollowupAsync(components: responseComponent, flags: MessageFlags.ComponentsV2).ConfigureAwait(false);
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

    [ComponentInteraction("assistant:ud_page:*:*:*:*", true)]
    public async Task HandlePageButtonAsync(ulong requesterId, string encodedSearchTerm, int currentPage,
        string action)
    {
        if (Context.User.Id != requesterId)
        {
            await RespondAsync("This interaction is not for you.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync().ConfigureAwait(false);

        string? searchTerm;
        try
        {
            var base64 = encodedSearchTerm.Replace('-', '+').Replace('_', '/');
            searchTerm = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            if (searchTerm == "_RANDOM_")
                searchTerm = null;
        }
        catch (FormatException)
        {
            await ModifyOriginalResponseAsync(props =>
            {
                props.Content = "Error: Invalid search term in button. Please try the command again.";
                props.Components = new ComponentBuilder().Build(); // Clear components
            }).ConfigureAwait(false);
            logger.LogWarning("Invalid Base64 search term in UD pagination button: {EncodedTerm}", encodedSearchTerm);
            return;
        }


        var results = await urbanService.GetDefinitionsAsync(searchTerm).ConfigureAwait(false);

        if (results == null || results.Count == 0)
        {
            await ModifyOriginalResponseAsync(props =>
            {
                props.Content =
                    "The definitions for this search seem to have expired from the cache. Please run the command again.";
                props.Components = new ComponentBuilder().Build();
            }).ConfigureAwait(false);
            return;
        }

        var newPage = action switch
        {
            "prev" => currentPage - 1,
            "next" => currentPage + 1,
            _ => currentPage
        };

        var newComponent = BuildDefinitionResponse(results, newPage, requesterId, searchTerm ?? "_RANDOM_");

        await ModifyOriginalResponseAsync(props =>
        {
            props.Content = ""; // Clear any previous error content
            props.Components = newComponent;
            props.Flags = MessageFlags.ComponentsV2;
        }).ConfigureAwait(false);
    }
}