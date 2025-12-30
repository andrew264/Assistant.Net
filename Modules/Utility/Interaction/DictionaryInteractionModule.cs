using System.Text;
using Assistant.Net.Services.ExternalApis;
using Assistant.Net.Utilities;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Utility.Interaction;

public class DictionaryInteractionModule(
    UrbanDictionaryService urbanService,
    ILogger<DictionaryInteractionModule> logger)
    : InteractionModuleBase<SocketInteractionContext>
{
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
            DictionaryUtils.BuildDefinitionResponse(results, 0, Context.User.Id, word ?? "_RANDOM_");

        await FollowupAsync(components: responseComponent, flags: MessageFlags.ComponentsV2).ConfigureAwait(false);
    }

    [ComponentInteraction(DictionaryUtils.CustomIdPrefix + ":*:*:*:*", true)]
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

        var newComponent =
            DictionaryUtils.BuildDefinitionResponse(results, newPage, requesterId, searchTerm ?? "_RANDOM_");

        await ModifyOriginalResponseAsync(props =>
        {
            props.Content = ""; // Clear any previous error content
            props.Components = newComponent;
            props.Flags = MessageFlags.ComponentsV2;
        }).ConfigureAwait(false);
    }
}