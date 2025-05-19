using Assistant.Net.Services;
using Assistant.Net.Utilities;
using Discord;
using Discord.Commands;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Misc;

public class DictionaryModule(
    UrbanDictionaryService urbanService,
    ILogger<DictionaryModule> logger)
    : ModuleBase<SocketCommandContext>
{
    private readonly Random _random = new();

    [Command("define", RunMode = RunMode.Async)]
    [Alias("def", "dictionary", "dict")]
    [Summary("Define a word using Urban Dictionary.")]
    public async Task DefinePrefixAsync([Remainder] string? word = null)
    {
        var results = await urbanService.GetDefinitionsAsync(word).ConfigureAwait(false);

        if (results == null) // Error
        {
            await ReplyAsync("Sorry, I couldn't fetch the definition due to an error.",
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

        // Select random
        var topResults = results.Take(5).ToList();
        var selectedEntry = topResults[_random.Next(topResults.Count)];
        var markdown = selectedEntry.Markdown;

        await SendDefinitionResponseAsync(Context, markdown).ConfigureAwait(false);
    }

    private async Task SendDefinitionResponseAsync(SocketCommandContext context, string markdown)
    {
        const int maxLen = DiscordConfig.MaxMessageSize;

        if (markdown.Length <= maxLen)
        {
            await context.Message.ReplyAsync(markdown, allowedMentions: AllowedMentions.None,
                flags: MessageFlags.SuppressEmbeds).ConfigureAwait(false);
        }
        else
        {
            logger.LogInformation(
                "Definition exceeds {MaxLength} characters, attempting to split and send (prefix command).", maxLen);
            var parts = MessageUtils.SplitMessage(markdown, maxLen); // TODO: SmartSplit

            // Send first part via reply
            var lastMessage = await context.Message.ReplyAsync(parts[0], allowedMentions: AllowedMentions.None,
                flags: MessageFlags.SuppressEmbeds).ConfigureAwait(false);

            for (var i = 1; i < parts.Count; i++)
            {
                await Task.Delay(250).ConfigureAwait(false);
                lastMessage = await lastMessage.ReplyAsync(
                    parts[i],
                    allowedMentions: AllowedMentions.None,
                    flags: MessageFlags.SuppressEmbeds
                ).ConfigureAwait(false);
            }
        }
    }
}