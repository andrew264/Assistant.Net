using Assistant.Net.Services;
using Assistant.Net.Utilities;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;

namespace Assistant.Net.Modules.Music.Autocomplete;

public class MusicQueryAutocompleteProvider : AutocompleteHandler
{
    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction,
        IParameterInfo parameter,
        IServiceProvider services)
    {
        if (context.Guild == null)
            return AutocompletionResult.FromSuccess([]);

        var musicHistoryService = services.GetRequiredService<MusicHistoryService>();
        var userInput = autocompleteInteraction.Data.Current.Value as string ?? string.Empty;

        var suggestions = new List<AutocompleteResult>
        {
            !string.IsNullOrWhiteSpace(userInput)
                ? new AutocompleteResult($"Search: {userInput}", userInput)
                : new AutocompleteResult("Search for a song or paste a URL...", string.Empty)
        };

        // Add suggestions from history if user has typed something
        if (string.IsNullOrWhiteSpace(userInput)) return AutocompletionResult.FromSuccess(suggestions.Take(25));
        var historyResults = await musicHistoryService.SearchSongHistoryAsync(context.Guild.Id, userInput)
            .ConfigureAwait(false);
        foreach (var entry in historyResults)
        {
            if (suggestions.Count >= 25) break;
            if (entry.Uri != userInput)
                suggestions.Add(new AutocompleteResult(
                    entry.Title.Truncate(90),
                    entry.Uri));
        }

        return AutocompletionResult.FromSuccess(suggestions.Take(25));
    }
}