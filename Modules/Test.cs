using Discord.Commands;

namespace Assistant.Net.Modules;

public class TestModule : ModuleBase<SocketCommandContext>
{
    [Command("test")]
    public async Task TestAsync()
        => await ReplyAsync("Test command executed!");
}