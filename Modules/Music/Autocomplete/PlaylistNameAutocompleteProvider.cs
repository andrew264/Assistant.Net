using Assistant.Net.Services.Music;
using Assistant.Net.Utilities;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;

namespace Assistant.Net.Modules.Music.Autocomplete;

public class PlaylistNameAutocompleteProvider : AutocompleteHandler
{
    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction,
        IParameterInfo parameter,
        IServiceProvider services)
    {
        if (context.Guild == null) return AutocompletionResult.FromSuccess();

        var playlistService = services.GetRequiredService<PlaylistService>();
        var currentInput = autocompleteInteraction.Data.Current.Value as string ?? "";

        var playlists = await playlistService.GetUserPlaylistsAsync(context.User.Id, context.Guild.Id)
            .ConfigureAwait(false);

        var choices = playlists
            .Where(p => p.Name.Contains(currentInput, StringComparison.OrdinalIgnoreCase))
            .Select(p =>
                new AutocompleteResult(p.Name.Truncate(100), p.Name))
            .Take(25);

        return AutocompletionResult.FromSuccess(choices);
    }
}