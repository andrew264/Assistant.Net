using Discord;
using Discord.Interactions;
using Discord.WebSocket;

namespace Assistant.Net.Modules.Info;

public class InfoModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly DiscordSocketClient _client;

    public InfoModule(DiscordSocketClient client)
    {
        _client = client;
    }


    [SlashCommand("ping", "Check the bot's latency.")]
    public async Task PingAsync()
    {
        var latency = _client.Latency;
        await RespondAsync($"Pong! Latency: {latency}ms", ephemeral: true);
    }

    [SlashCommand("info", "Get information about the bot.")]
    public async Task InfoAsync()
    {
        var embed = new EmbedBuilder()
            .WithTitle("Bot Information")
            .WithDescription("This is a test embed.")
            .WithColor(Color.Blue)
            .WithCurrentTimestamp();

        await RespondAsync(embed: embed.Build(), ephemeral: true);
    }
}