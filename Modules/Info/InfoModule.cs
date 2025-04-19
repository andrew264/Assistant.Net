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
}