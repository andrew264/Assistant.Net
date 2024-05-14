using Assistant.Net.Utils;
using Discord;
using Discord.Commands;
using Discord.Webhook;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using System.Text;

namespace Assistant.Net.Modules;

public class BirdWatcherModule : ModuleBase<SocketCommandContext>
{
    private readonly Config _config;
    private readonly DiscordSocketClient _client;
    private readonly HttpClient _httpClient;

    private readonly ulong GuildID;
    private readonly ulong ChannelID;

    public BirdWatcherModule(IServiceProvider services)
    {
        _config = services.GetRequiredService<Config>();
        _client = services.GetRequiredService<DiscordSocketClient>();
        _httpClient = services.GetRequiredService<HttpClient>();

        GuildID = _config.client.home_guild_id;
        ChannelID = _config.client.logging_channel_id;

        _client.MessageUpdated += MessageEditWatcherAsync;
    }

    private async Task<DiscordWebhookClient?> GetOrCreateWebhookAsync()
    {
        if (_client.GetChannel(ChannelID) is not ITextChannel channel) return null;

        var webhooks = await channel.GetWebhooksAsync();
        var webhook = webhooks.FirstOrDefault(w => w.Name == "Assistant");
        if (webhook != null)
        {
            return new DiscordWebhookClient(webhook);
        }

        var avatarUrl = _client.CurrentUser.GetAvatarUrl(size: 128);
        using var avatarStream = await _httpClient.GetStreamAsync(avatarUrl);
        webhook = await channel.CreateWebhookAsync("Assistant", avatarStream);

        return new DiscordWebhookClient(webhook);
    }

    private async Task MessageEditWatcherAsync(Cacheable<IMessage, ulong> before, SocketMessage after, ISocketMessageChannel channel)
    {
        if (after.Author.IsBot || after.Author.IsWebhook || channel is not IGuildChannel messageChannel || messageChannel.Guild.Id != GuildID)
        {
            return;
        }

        var beforeMessage = await before.GetOrDownloadAsync();
        if (beforeMessage == null || beforeMessage.Content == after.Content)
        {
            return;
        }

        var sb = new StringBuilder()
            .AppendLine($"**Message edited in <#{after.Channel.Id}>**")
            .AppendLine("**Before**")
            .AppendLine($"```{beforeMessage.Content}```")
            .AppendLine("**After**")
            .AppendLine($"```{after.Content}```")
            .AppendLine($"[Jump to message]({after.GetJumpUrl()})");

        var webhook = await GetOrCreateWebhookAsync();
        if (webhook != null)
        {
            await webhook.SendMessageAsync(text: sb.ToString(), username: after.Author.Username, avatarUrl: after.Author.GetDisplayAvatarUrl());
        }
    }
}
