using Assistant.Net.Services.Data;
using Assistant.Net.Utilities;
using Discord;
using Discord.Interactions;

namespace Assistant.Net.Modules.Games.Autocomplete;

public class GameAutocompleteProvider : AutocompleteHandler
{
    public static readonly List<string> GameNames =
    [
        GameStatsService.TicTacToeGameName,
        GameStatsService.RpsGameName,
        GameStatsService.HandCricketGameName
    ];

    public override Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        var userInput = autocompleteInteraction.Data.Current.Value as string ?? string.Empty;

        var suggestions = GameNames
            .Where(name => name.StartsWith(userInput, StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name)
            .Select(name => new AutocompleteResult(name.CapitalizeFirstLetter(), name))
            .Take(25); // Discord limit

        return Task.FromResult(AutocompletionResult.FromSuccess(suggestions));
    }
}