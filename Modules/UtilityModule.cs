using Discord.Interactions;

namespace Assistant.Net.Modules;

public class UtilityModule : InteractionModuleBase<SocketInteractionContext>
{
    public InteractionService Commands { get; set; }

    private readonly InteractionHandler _handler;

    public UtilityModule(InteractionHandler handler)
    {
        _handler = handler;
    }

    [SlashCommand("ping", "Pings the bot and returns its latency.")]
    public async Task GreetUserAsync()
        => await RespondAsync(text: $"Pong! {Context.Client.Latency}ms", ephemeral: true);
}
