using Discord;
using Discord.Commands;
using Discord.Webhook;

namespace Assistant.Net.Modules.Prefix;

public class TestModule : ModuleBase<SocketCommandContext>
{
    public required HttpClient _httpClient { get; set; }


    [Command("test", RunMode = RunMode.Async)]
    public async Task TestAsync()
        => await ReplyAsync("Test command executed!");

    [RequireOwner]
    [Command("reaction_roles", RunMode = RunMode.Async)]
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

    private static Task<IMessage?> MessageValidToDeleteAsync(IMessage message, string[] AllTags)
    {
        return Task.Run(() =>
        {
            for (int i = 0; i < AllTags.Length; i++)
            {
                if (message.Author.Id.ToString().Contains(AllTags[i], StringComparison.CurrentCultureIgnoreCase) ||
                    message.Author.GlobalName != null && message.Author.GlobalName.Contains(AllTags[i], StringComparison.CurrentCultureIgnoreCase) ||
                    message.Author.Username.Contains(AllTags[i], StringComparison.CurrentCultureIgnoreCase) ||
                    message.Content.Contains(AllTags[i], StringComparison.CurrentCultureIgnoreCase))
                    return message;

                if (message.Attachments.Count > 0)
                    foreach (var attachment in message.Attachments)
                        if (attachment.Filename.Contains(AllTags[i], StringComparison.CurrentCultureIgnoreCase))
                            return message;

                if (message.Embeds.Count > 0)
                    foreach (var embed in message.Embeds)
                    {
                        if (embed.Description != null && embed.Description.Contains(AllTags[i], StringComparison.CurrentCultureIgnoreCase))
                            return message;

                        for (int j = 0; j < embed.Fields.Length; j++)
                            if (embed.Fields[j].Name.Contains(AllTags[i], StringComparison.CurrentCultureIgnoreCase) ||
                                embed.Fields[j].Value.Contains(AllTags[i], StringComparison.CurrentCultureIgnoreCase))
                                return message;

                        if (embed.Title != null && embed.Title.Contains(AllTags[i], StringComparison.CurrentCultureIgnoreCase))
                            return message;

                        if (embed.Author.HasValue && embed.Author.Value.Name.Contains(AllTags[i], StringComparison.CurrentCultureIgnoreCase))
                            return message;

                        if (embed.Footer.HasValue && embed.Footer.Value.Text.Contains(AllTags[i], StringComparison.CurrentCultureIgnoreCase))
                            return message;
                    };
            }
            return null;
        });
    }


    [RequireOwner]
    [Command("yeet", Aliases = ["purge", "begon"], RunMode = RunMode.Async)]
    [RequireContext(ContextType.Guild)]
    [RequireBotPermission(GuildPermission.ManageMessages)]
    public async Task PurgeMessagesAsync([Remainder] string tags)
    {
        await Context.Message.DeleteAsync();
        string[] AllTags = tags.Split(',').Select(x => x.Trim().ToLower()).ToArray();
        if (AllTags.Length == 0)
        {
            await ReplyAsync("No tags provided.");
            return;
        }
        if (Context.Channel is not ITextChannel channel)
        {
            await ReplyAsync("This command can only be used in a guild channel");
            return;
        }
        var messages = await channel.GetMessagesAsync(10_000).FlattenAsync();
        var tasks = messages.Select(message => MessageValidToDeleteAsync(message, AllTags)).ToArray();
        var results = await Task.WhenAll(tasks);

        var toDelete = results.Where(validMessage => validMessage != null).ToList();

        if (toDelete.Count == 0)
        {
            await ReplyAsync("No messages found with the specified tags.");
            return;
        }

        var group1 = toDelete.Where(x => (DateTimeOffset.UtcNow - x.Timestamp).TotalDays < 14).ToList();
        var group2 = toDelete.Where(x => (DateTimeOffset.UtcNow - x.Timestamp).TotalDays >= 14).ToList();
        try
        {
            if (group1.Count > 0)
                await channel.DeleteMessagesAsync(group1);
            if (group2.Count > 0)
                foreach (var message in group2)
                    if (message is not null)
                        try
                        {
                            await message.DeleteAsync();
                        }
                        catch (Exception)
                        {
                            continue;
                        }
        }
        catch (Exception e)
        {
            await ReplyAsync($"Failed to delete messages: {e.Message}");
        }

        var msg = await channel.SendMessageAsync($"Deleted {toDelete.Count} messages.");
        _ = Task.Delay(5000).ContinueWith(async _ => await msg.DeleteAsync());
    }

    private static async Task<DiscordWebhookClient> GetOrCreateWebhookAsync(ITextChannel channel)
    {
        var webhooks = await channel.GetWebhooksAsync();
        var webhook = webhooks.FirstOrDefault(x => x.Name == "Assistant");

        if (webhook != null)
            return new DiscordWebhookClient(webhook);

        var newWebhook = await channel.CreateWebhookAsync("Assistant");
        return new DiscordWebhookClient(newWebhook);
    }

    [RequireOwner]
    [Command("echo", RunMode = RunMode.Async)]
    [RequireContext(ContextType.Guild)]
    [RequireBotPermission(GuildPermission.ManageWebhooks)]
    public async Task EchoAsync([Remainder] string message = "")
    {
        if (string.IsNullOrWhiteSpace(message) && Context.Message.Attachments.Count == 0)
        {
            await ReplyAsync("No message provided.");
            return;
        }

        if (Context.Channel is not ITextChannel channel)
        {
            await ReplyAsync("This command can only be used in a guild channel.");
            return;
        }

        var webhook = await GetOrCreateWebhookAsync(channel);

        if (Context.Message.Attachments.Count == 0)
        {
            await Context.Message.DeleteAsync();
            await webhook.SendMessageAsync(message, username: Context.User.Username, avatarUrl: Context.User.GetAvatarUrl(size: 128));
        }
        else
        {
            var fileAttachments = await Task.WhenAll(Context.Message.Attachments.Select(async attachment =>
            {
                var stream = await _httpClient.GetStreamAsync(attachment.Url);
                return new FileAttachment(stream, attachment.Filename);
            }));

            await Context.Message.DeleteAsync();
            await webhook.SendFilesAsync(fileAttachments, text: message, username: Context.User.Username, avatarUrl: Context.User.GetAvatarUrl(size: 128));
        }
    }
}