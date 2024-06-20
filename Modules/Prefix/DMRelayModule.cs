using Discord;
using Discord.Commands;
using Discord.Webhook;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;

namespace Assistant.Net.Modules.Prefix;

public class DMRelayModule : ModuleBase<SocketCommandContext>
{
    private readonly BotConfig _config;
    private readonly DiscordSocketClient _client;
    private readonly HttpClient _httpClient;
    private readonly ulong GuildID;
    private readonly ulong CategoryID;

    public DMRelayModule(IServiceProvider services)
    {
        _config = services.GetRequiredService<BotConfig>();
        _client = services.GetRequiredService<DiscordSocketClient>();
        _httpClient = services.GetRequiredService<HttpClient>();
        GuildID = _config.client.home_guild_id;
        CategoryID = _config.client.dm_category_id;

        _client.MessageReceived += SendDMFromCategoryAsync;
        _client.MessageReceived += RecieveDMToCategoryAsync;
    }

    private async Task<IWebhook?> GetOrCreateWebhook(IUser user)
    {
        var guild = _client.GetGuild(GuildID);
        if (guild == null) return null;

        var category = guild.GetCategoryChannel(CategoryID);
        if (category == null) return null;

        var channel = category.Channels.FirstOrDefault(c => c is ITextChannel text && text.Topic == user.Id.ToString());
        if (channel is ITextChannel textChannel)
        {
            var webhooks = await textChannel.GetWebhooksAsync();
            return webhooks.FirstOrDefault();
        }

        ulong ClientID = _client.CurrentUser.Id;
        var DMChannel = await guild.CreateTextChannelAsync(user.Username, x =>
        {
            x.CategoryId = CategoryID;
            x.Topic = user.Id.ToString();
            x.PermissionOverwrites = new List<Overwrite>()
            {
                new(guild.EveryoneRole.Id, PermissionTarget.Role, new OverwritePermissions(viewChannel: PermValue.Deny)),
                new(ClientID, PermissionTarget.User, new OverwritePermissions(viewChannel: PermValue.Allow))
            };
        });
        Stream avatar = _httpClient.GetStreamAsync(user.GetAvatarUrl(size: 128)).Result;
        return await DMChannel.CreateWebhookAsync("Assistant", avatar);
    }

    private async Task<FileAttachment[]> GetFileAttachments(IEnumerable<Attachment>? attachments)
    {
        var fileAttachments = new List<FileAttachment>();
        if (attachments == null) return [.. fileAttachments];
        foreach (var attachment in attachments)
        {
            using var stream = await _httpClient.GetStreamAsync(attachment.Url);
            fileAttachments.Add(new FileAttachment(stream: stream, attachment.Filename));
        }
        return [.. fileAttachments];
    }

    private async Task<FileAttachment[]> GetStickersAsAttachments(IEnumerable<SocketSticker>? stickers)
    {
        var fileAttachments = new List<FileAttachment>();
        if (stickers == null) return [.. fileAttachments];
        foreach (var sticker in stickers)
        {
            try { sticker.GetStickerUrl(); }  // Throws if sticker is a gif
            catch (ArgumentException) { continue; }

            using var stream = await _httpClient.GetStreamAsync(sticker.GetStickerUrl());
            fileAttachments.Add(new FileAttachment(stream: stream, sticker.Name + $".{sticker.Format.ToString().ToLower()}"));
        }
        return [.. fileAttachments];
    }

    [Command("dm", RunMode = RunMode.Async)]
    [RequireOwner]
    public async Task DMRelay(SocketUser socketUser, [Remainder] string message = "")
    {
        if (message.Length == 0 && Context.Message.Attachments.Count == 0 && Context.Message.Stickers.Count == 0) return;

        var attachments = await GetFileAttachments(Context.Message.Attachments);
        attachments = [.. attachments, .. await GetStickersAsAttachments(Context.Message.Stickers)];


        if (attachments.Length == 0)
            await socketUser.SendMessageAsync(text: message);
        else
            await socketUser.SendFilesAsync(attachments: attachments, text: message);

        var webhook = await GetOrCreateWebhook(socketUser);
        if (webhook == null) return;

        using var webhookClient = new DiscordWebhookClient(webhook);
        if (attachments.Length == 0)
            await webhookClient.SendMessageAsync(text: message, username: Context.User.Username, avatarUrl: Context.User.GetAvatarUrl(size: 128));
        else
            await webhookClient.SendFilesAsync(attachments: await GetFileAttachments(Context.Message.Attachments), text: message, username: Context.User.Username, avatarUrl: Context.User.GetAvatarUrl(size: 128));
    }

    public async Task SendDMFromCategoryAsync(SocketMessage message)
    {
        if (message.Channel is not ITextChannel channel) return;
        if (channel.CategoryId != CategoryID) return;
        if (message.Author.IsBot) return;
        if (message.Author.Id != _client.GetApplicationInfoAsync().Result.Owner.Id) return;

        var topic = channel.Topic;
        if (topic == null) return;

        var user = _client.GetUser(ulong.Parse(topic));
        if (user == null) return;

        if (message.Content.Length == 0 && message.Attachments.Count == 0 && message.Stickers.Count == 0) return;

        var attachments = await GetFileAttachments(message.Attachments);
        attachments = [.. attachments, .. await GetStickersAsAttachments(message.Stickers)];


        if (attachments.Length == 0)
            await user.SendMessageAsync(text: message.Content);
        else
            await user.SendFilesAsync(attachments: attachments, text: message.Content);
    }

    public async Task RecieveDMToCategoryAsync(SocketMessage message)
    {
        if (message.Channel is not IDMChannel dmChannel) return;
        if (dmChannel.Recipient.IsBot) return;
        var user = dmChannel.Recipient;

        if (message.Content.Length == 0 && message.Attachments.Count == 0 && message.Stickers.Count == 0) return;

        var attachments = await GetFileAttachments(message.Attachments);
        attachments = [.. attachments, .. await GetStickersAsAttachments(message.Stickers)];


        var webhook = await GetOrCreateWebhook(user);
        if (webhook == null) return;

        using var webhookClient = new DiscordWebhookClient(webhook);
        if (attachments.Length == 0)
            await webhookClient.SendMessageAsync(text: message.Content, username: user.Username, avatarUrl: user.GetAvatarUrl(size: 128));
        else
            await webhookClient.SendFilesAsync(attachments: attachments, text: message.Content, username: user.Username, avatarUrl: user.GetAvatarUrl(size: 128));
    }
}
