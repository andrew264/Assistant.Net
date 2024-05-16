using Discord;
using Discord.Interactions;

namespace Assistant.Net.Modules;

[CommandContextType([InteractionContextType.Guild])]
public class AdminCommandModule : InteractionModuleBase<SocketInteractionContext>
{
    public required InteractionService Commands { get; set; }

    [SlashCommand("clear", "Clears the specified amount of messages")]
    [RequireBotPermission(GuildPermission.ManageMessages)]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task ClearMessagesAsync(
               [Summary(description: "Enter the number of messages to clear")][MinValue(1)][MaxValue(420)] int amount = 5)
    {
        if (Context.Channel is not ITextChannel channel)
        {
            await RespondAsync("This command can only be used in a guild channel", ephemeral: true);
            return;
        }
        await DeferAsync(ephemeral: true);
        var messages = (await channel.GetMessagesAsync(amount + 1).FlattenAsync()).Where(x => (DateTimeOffset.UtcNow - x.Timestamp).TotalDays < 14 && x.Flags is not MessageFlags.Ephemeral);

        if (!messages.TryGetNonEnumeratedCount(out int count))
            count = messages.Count();

        if (count <= 1)
        {
            await ModifyOriginalResponseAsync(x => x.Content = "Couldn't get any deletable messages");
            return;
        }
        await channel.DeleteMessagesAsync(messages);
        await ModifyOriginalResponseAsync(x => x.Content = $"Deleted {count} messages");
    }

    [MessageCommand("Delete messages below")]
    [RequireBotPermission(GuildPermission.ManageMessages)]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task DeleteTillHereAsync(IMessage message)
    {
        if (Context.Channel is not ITextChannel channel)
        {
            await RespondAsync("This command can only be used in a guild channel", ephemeral: true);
            return;
        }
        await DeferAsync(ephemeral: true);
        var messages = (await Context.Channel.GetMessagesAsync(message, Direction.After).FlattenAsync()).Where(x => (DateTimeOffset.UtcNow - x.Timestamp).TotalDays < 14 && x.Flags is not MessageFlags.Ephemeral);

        if (!messages.TryGetNonEnumeratedCount(out int count))
            count = messages.Count();

        if (count <= 1)
        {
            await ModifyOriginalResponseAsync(x => x.Content = "Couldn't get any deletable messages");
            return;
        }

        await channel.DeleteMessagesAsync(messages);
        await ModifyOriginalResponseAsync(x => x.Content = $"Deleted {messages.Count()} messages");
    }

}
