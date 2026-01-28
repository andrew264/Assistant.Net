using System.Text.Json;
using Assistant.Net.Options;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Net.Modules.Games.Interaction;

internal record DeathMessagesConfig
{
    public List<string> Messages { get; init; } = [];
}

public class FunCommands : InteractionModuleBase<SocketInteractionContext>
{
    private static List<string>? _deathMsgsTemplate;
    private static readonly Lock TemplateLock = new();
    private static readonly List<string> Flames = ["Friends", "Lovers", "Angry", "Married", "Enemies", "Soulmates"];
    private static readonly Random Random = new();

    private readonly DiscordSocketClient _client;
    private readonly DiscordOptions _options;

    public FunCommands(DiscordSocketClient client, ILogger<FunCommands> logger, IOptions<DiscordOptions> options)
    {
        _client = client;
        _options = options.Value;

        // Lazy-load the death messages from the JSON file
        if (_deathMsgsTemplate != null) return;
        lock (TemplateLock)
        {
            _deathMsgsTemplate ??= LoadDeathMessages(_options.ResourcePath, logger);
        }
    }

    private static List<string> LoadDeathMessages(string resourcePath, ILogger logger)
    {
        const string fileName = "death-messages.json";
        var filePath = Path.Combine(resourcePath, fileName);
        List<string> defaultMessages = ["{user1} was killed by {user2}."];

        if (!File.Exists(filePath))
        {
            logger.LogWarning("Death message file not found at '{Path}'. Using default death message.", filePath);
            return defaultMessages;
        }

        try
        {
            var content = File.ReadAllText(filePath);
            var config = JsonSerializer.Deserialize<DeathMessagesConfig>(content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (config?.Messages.Count > 0)
            {
                logger.LogInformation("[LOADED] {Count} death messages from {FileName}", config.Messages.Count,
                    fileName);
                return config.Messages;
            }

            logger.LogWarning("'{FileName}' is empty or malformed. Using default death message.", fileName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse '{FileName}'. Using default death message.", fileName);
        }

        return defaultMessages;
    }

    [SlashCommand("kill", "Delete their existence.")]
    [RequireContext(ContextType.Guild)]
    public async Task KillAsync([Summary("user", "Who should I kill?")] SocketUser user)
    {
        if (user.Id == Context.User.Id)
        {
            await RespondAsync("Stop, Get some Help.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var description = user.IsBot
            ? "You cannot attack my kind."
            : GetDeathMessage(user, Context.User);

        var container = new ContainerBuilder()
            .WithSection(section =>
            {
                section.AddComponent(new TextDisplayBuilder($"# R.I.P. {user.Username}"));
                section.AddComponent(new TextDisplayBuilder(description));
                section.WithAccessory(new ThumbnailBuilder
                {
                    Media = new UnfurledMediaItemProperties
                        { Url = user.GetDisplayAvatarUrl() ?? user.GetDefaultAvatarUrl() }
                });
            });

        var components = new ComponentBuilderV2().WithContainer(container).Build();

        await RespondAsync(components: components, flags: MessageFlags.ComponentsV2).ConfigureAwait(false);
    }

    private string GetDeathMessage(SocketUser target, SocketUser killer)
    {
        if (Random.NextDouble() < 0.1)
            (target, killer) = (killer, target);

        var template = _deathMsgsTemplate![Random.Next(_deathMsgsTemplate.Count)];

        return template
            .Replace("{user1}", target.Mention)
            .Replace("{user2}", killer.Mention)
            .Replace("{user3}", _client.CurrentUser.Mention);
    }

    [SlashCommand("pp", "Check your pp size")]
    [RequireContext(ContextType.Guild)]
    public async Task PpAsync([Summary("user", "Whose pp should I check?")] SocketGuildUser? user = null)
    {
        user ??= Context.User as SocketGuildUser;

        if (user is null)
        {
            await RespondAsync("Could not determine the user.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var description = user.Id == _options.OwnerId
            ? $"[8{new string('=', Random.Next(7, 13))}D](https://www.youtube.com/watch?v=dQw4w9WgXcQ \"Ran out of Tape while measuring\")"
            : user.IsBot
                ? "404 Not Found"
                : $"8{new string('=', Random.Next(0, 10))}D";

        var container = new ContainerBuilder()
            .WithSection(section =>
            {
                section.AddComponent(new TextDisplayBuilder($"# {user.DisplayName}'s PP Size"));
                section.AddComponent(new TextDisplayBuilder(description));
                section.WithAccessory(new ThumbnailBuilder
                {
                    Media = new UnfurledMediaItemProperties
                        { Url = user.GetGuildAvatarUrl() ?? user.GetDisplayAvatarUrl() }
                });
            });

        var components = new ComponentBuilderV2().WithContainer(container).Build();

        await RespondAsync(components: components, flags: MessageFlags.ComponentsV2).ConfigureAwait(false);
    }

    [SlashCommand("flames", "Check your relationship with someone")]
    [RequireContext(ContextType.Guild)]
    public async Task FlamesAsync(
        [Summary("first-person", "Who is the first person?")]
        string user1,
        [Summary("second-person", "Who is the second person?")]
        string? user2 = null)
    {
        var name1 = user1.ToLowerInvariant().Replace(" ", "");
        var name2 = (user2 ?? Context.User.GlobalName).ToLowerInvariant().Replace(" ", "");

        if (name1 == name2)
        {
            await RespondAsync("Stop, Get some Help.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var chars1 = new List<char>(name1);
        var chars2 = new List<char>(name2);

        for (var i = 0; i < chars1.Count; i++)
        {
            var index = chars2.IndexOf(chars1[i]);
            if (index == -1) continue;
            chars1.RemoveAt(i--);
            chars2.RemoveAt(index);
        }

        var count = chars1.Count + chars2.Count;

        if (count == 0)
        {
            await RespondAsync("Cannot determine relationship with these names.", ephemeral: true)
                .ConfigureAwait(false);
            return;
        }

        var result = Flames[count % Flames.Count];

        var container = new ContainerBuilder()
            .WithTextDisplay(new TextDisplayBuilder($"# FLAMES: {user1} & {user2 ?? Context.User.GlobalName}"))
            .WithSeparator()
            .WithTextDisplay(new TextDisplayBuilder($"Your relationship is... **{result}**"));

        var components = new ComponentBuilderV2().WithContainer(container).Build();

        await RespondAsync(components: components, flags: MessageFlags.ComponentsV2).ConfigureAwait(false);
    }
}