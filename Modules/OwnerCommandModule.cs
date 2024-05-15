using Discord;
using Discord.Commands;

namespace Assistant.Net.Modules;

public class TestModule : ModuleBase<SocketCommandContext>
{
    [Command("test")]
    public async Task TestAsync()
        => await ReplyAsync("Test command executed!");

    [RequireOwner]
    [Command("reaction_roles")]
    [RequireContext(ContextType.Guild)]
    public async Task ColorAsync()
    {
        await Context.Message.DeleteAsync();

        var embed = new EmbedBuilder()
            .WithTitle("Reaction Roles")
            .WithDescription("Claim a colour of your choice!")
            .WithColor(new Color(0xFFFFFF))
            .Build();

        var component = new ComponentBuilder()
            .WithButton(style: ButtonStyle.Secondary, emote: new Emoji("🟥"), customId: "assistant:addrole:891766305470971984")
            .WithButton(style: ButtonStyle.Secondary, emote: new Emoji("🟩"), customId: "assistant:addrole:891766413721759764")
            .WithButton(style: ButtonStyle.Secondary, emote: new Emoji("🟦"), customId: "assistant:addrole:891766503219798026")
            .WithButton(style: ButtonStyle.Secondary, emote: new Emoji("🟫"), customId: "assistant:addrole:891782414412697600")
            .WithButton(style: ButtonStyle.Secondary, emote: new Emoji("🟪"), customId: "assistant:addrole:891782622374678658")
            .WithButton(style: ButtonStyle.Secondary, emote: new Emoji("🟨"), customId: "assistant:addrole:891782804008992848")
            .WithButton(style: ButtonStyle.Secondary, emote: new Emoji("🟧"), customId: "assistant:addrole:891783123711455292")
            .Build();

        await Context.Channel.SendMessageAsync(embed: embed, components: component);
    }
}