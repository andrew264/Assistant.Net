using Discord.Interactions;
using Discord.WebSocket;

namespace Assistant.Net.Modules;

public class MiniGameModule : InteractionModuleBase<SocketInteractionContext>
{
    public required InteractionService Commands { get; set; }
    private static readonly Random random = new();
    private static readonly List<string> flames = ["Friends", "Lovers", "Angry", "Married", "Enemies", "Siblings"];

    [SlashCommand("pp", "Measure your pp size")]
    public async Task PpAsync([Summary(description: "Mention a user to measure their pp size")] SocketUser? user = null)
    {
        user ??= Context.User;
        var application = await Context.Client.GetApplicationInfoAsync();
        if (user.IsBot)
        {
            await ReplyAsync($"404 Not Found\n{user.Username} does not have a PP");
        }
        else if (user.Id == application.Owner.Id)
        {
            await ReplyAsync($"## {user.Username}'s PP:\n### [8{new string('=', 13)}D](<https://www.youtube.com/watch?v=dQw4w9WgXcQ> \"Ran out of Tape while measuring\")");
        }
        else
        {
            await ReplyAsync($"## {user.Username}'s PP:\n### 8{new string('=', random.Next(1, 8))}D");
        }
    }

    [SlashCommand("flames", "Check your relationship with someone")]
    public async Task FlamesAsync(
        [Summary(description: "Who is the first person?")] string user1,
        [Summary(description: "Who is the second person?")] string? user2 = null)
    {
        user2 ??= Context.User.Username;

        string originalUser1 = user1;
        string originalUser2 = user2;

        user1 = user1.ToLower().Replace(" ", "");
        user2 = user2.ToLower().Replace(" ", "");

        if (user1 == user2)
        {
            await ReplyAsync($"## Stop, Get some Help.");
            return;
        }

        foreach (char c in user1)
        {
            if (user2.Contains(c))
            {
                user2 = user2.Remove(user2.IndexOf(c), 1);
                user1 = user1.Remove(user1.IndexOf(c), 1);
            }
        }

        int flamesIndex = (user1.Length + user2.Length) % flames.Count;

        await ReplyAsync($"## {originalUser1} and {originalUser2} are {flames[flamesIndex]}");
    }


}
