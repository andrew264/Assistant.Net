using Discord.Interactions;

namespace Assistant.Net.Modules.Interaction;

public class UtilityModule : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("ping", "Pings the bot and returns its latency.")]
    public async Task GreetUserAsync()
        => await RespondAsync(text: $"Pong! {Context.Client.Latency}ms", ephemeral: true);

}
