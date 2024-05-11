using Discord;
using Discord.Interactions;

namespace Assistant.Net.Modules;


public class BotControlModule : InteractionModuleBase<SocketInteractionContext>
{
    public required InteractionService Commands { get; set; }

    [RequireOwner]
    [SlashCommand("setstatus", "Change Bot's Activity and Presence")]
    public async Task SetStatusAsync(
        [Summary(description: "Select Status")] UserStatus status,
        [Summary(description: "Select Activity Type")] ActivityType activityType,
        [Summary(description: "Enter Text")] string activity)
    {
        await Context.Client.SetGameAsync(activity, type: activityType);
        await Context.Client.SetStatusAsync(status);
        await RespondAsync("Status Changed!", ephemeral: true);
    }

}
