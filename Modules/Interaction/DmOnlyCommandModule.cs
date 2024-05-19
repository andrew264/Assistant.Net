using Discord;
using Discord.Interactions;

namespace Assistant.Net.Modules.Interaction;

[CommandContextType([InteractionContextType.BotDm])]
public class DmOnlyCommandModule : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("clear-dm", "Delete messages sent by the bot in DM")]
    public async Task ClearDmMessagesAsync()
    {
        if (Context.Channel is not IDMChannel channel)
        {
            await RespondAsync("This command can only be used in a DM channel", ephemeral: true);
            return;
        }
        await DeferAsync(ephemeral: true);
        var messages = (await channel.GetMessagesAsync(100).FlattenAsync()).Where(x => x.Author.Id == Context.Client.CurrentUser.Id);

        if (!messages.TryGetNonEnumeratedCount(out int count))
            count = messages.Count();

        if (count <= 1)
        {
            await ModifyOriginalResponseAsync(x => x.Content = "Couldn't get any deletable messages");
            return;
        }
        foreach (var message in messages)
            await message.DeleteAsync();

        await ModifyOriginalResponseAsync(x => x.Content = $"Deleted {count - 1} messages");
    }

}
