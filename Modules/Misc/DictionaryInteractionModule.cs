using Assistant.Net.Services;
using Assistant.Net.Utilities;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Misc;

public class DictionaryInteractionModule(
    UrbanDictionaryService urbanService,
    ILogger<DictionaryInteractionModule> logger)
    : InteractionModuleBase<SocketInteractionContext>
{
    private readonly Random _random = new();

    [SlashCommand("define", "Define a word using Urban Dictionary.")]
    public async Task DefineSlashAsync(
        [Summary("word", "The word to define (optional, gets random if empty)")]
        string? word = null)
    {
        await DeferAsync().ConfigureAwait(false);

        var results = await urbanService.GetDefinitionsAsync(word).ConfigureAwait(false);

        if (results == null)
        {
            await FollowupAsync("Sorry, I couldn't fetch the definition due to an error.", ephemeral: true).ConfigureAwait(false);
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

        // Select a random entry
        var topResults = results.Take(5).ToList();
        var selectedEntry = topResults[_random.Next(topResults.Count)];
        var markdown = selectedEntry.Markdown;

        await SendDefinitionResponseAsync(Context.Interaction, markdown).ConfigureAwait(false);
    }

    private async Task SendDefinitionResponseAsync(SocketInteraction interaction, string markdown)
    {
        const int maxLen = DiscordConfig.MaxMessageSize; // Use Discord constant

        if (markdown.Length <= maxLen)
        {
            await interaction.ModifyOriginalResponseAsync(p =>
            {
                p.Content = markdown;
                p.AllowedMentions = AllowedMentions.None;
                p.Flags = MessageFlags.SuppressEmbeds;
            }).ConfigureAwait(false);
        }
        else
        {
            logger.LogInformation(
                "Definition exceeds {MaxLength} characters, attempting to split and send (interaction).", maxLen);
            var parts = MessageUtils.SplitMessage(markdown, maxLen); // TODO: SmartSplit?

            await interaction.ModifyOriginalResponseAsync(p =>
            {
                p.Content = parts[0];
                p.AllowedMentions = AllowedMentions.None;
                p.Flags = MessageFlags.SuppressEmbeds;
            }).ConfigureAwait(false);


            for (var i = 1; i < parts.Count; i++)
                await interaction.FollowupAsync(parts[i], allowedMentions: AllowedMentions.None).ConfigureAwait(false);
        }
    }
}