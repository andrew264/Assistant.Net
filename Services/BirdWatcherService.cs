using Discord;
using Discord.Commands;
using Discord.Webhook;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using System.Text;

namespace Assistant.Net.Services;

public class BirdWatcherService : ModuleBase<SocketCommandContext>
{
    private readonly BotConfig _config;
    private readonly DiscordSocketClient _client;
    private readonly HttpClient _httpClient;
    private readonly MongoDbService _mongoDbService;

    private readonly ulong GuildID;
    private readonly ulong ChannelID;

    public BirdWatcherService(IServiceProvider services)
    {
        _config = services.GetRequiredService<BotConfig>();
        _client = services.GetRequiredService<DiscordSocketClient>();
        _httpClient = services.GetRequiredService<HttpClient>();
        _mongoDbService = services.GetRequiredService<MongoDbService>();

        GuildID = _config.client.home_guild_id;
        ChannelID = _config.client.logging_channel_id;

        _client.MessageUpdated += HandleMessageEditAsync;
        _client.MessageDeleted += HandleMessageDeleteAsync;
        _client.GuildMemberUpdated += HandleMemberNicknameChangeAsync;
        _client.UserUpdated += HandleUsernameChangeAsync;
        _client.PresenceUpdated += HandlePresenceUpdateAsync;
        _client.UserVoiceStateUpdated += HandleVoiceStateChangeAsync;
        _client.UserBanned += HandleUserBannedAsync;
        _client.UserLeft += HandleUserLeftAsync;
        _client.UserJoined += HandleUserJoinedAsync;
        _client.UserUnbanned += HandleUserUnbannedAsync;
    }

    private async Task<DiscordWebhookClient?> GetOrCreateWebhookAsync()
    {
        if (_client.GetChannel(ChannelID) is not ITextChannel channel) return null;

        var webhooks = await channel.GetWebhooksAsync();
        var webhook = webhooks.FirstOrDefault(w => w.Name == "Assistant") ?? await CreateWebhookAsync(channel);

        return webhook != null ? new DiscordWebhookClient(webhook) : null;
    }

    private async Task<IWebhook?> CreateWebhookAsync(ITextChannel channel)
    {
        var avatarUrl = _client.CurrentUser.GetAvatarUrl(size: 128);
        using var avatarStream = await _httpClient.GetStreamAsync(avatarUrl);
        return await channel.CreateWebhookAsync("Assistant", avatarStream);
    }

    private async Task SendMessageAsync(IUser user, string content)
    {
        var webhook = await GetOrCreateWebhookAsync();
        if (webhook != null)
            await webhook.SendMessageAsync(text: content, username: user.Username, avatarUrl: user.GetDisplayAvatarUrl());
    }

    private async Task HandleMessageEditAsync(Cacheable<IMessage, ulong> before, SocketMessage after, ISocketMessageChannel channel)
    {
        if (after.Author.IsBot || after.Author.IsWebhook || channel is not IGuildChannel messageChannel || messageChannel.Guild.Id != GuildID) return;

        var beforeMessage = await before.GetOrDownloadAsync();
        if (beforeMessage == null || beforeMessage.Content == after.Content) return;

        var sb = new StringBuilder()
            .AppendLine($"**Message edited in <#{after.Channel.Id}>**")
            .AppendLine("**Before**")
            .AppendLine($"```{beforeMessage.Content}```")
            .AppendLine("**After**")
            .AppendLine($"```{after.Content}```")
            .AppendLine($"[Jump to message]({after.GetJumpUrl()})");

        await SendMessageAsync(after.Author, sb.ToString());
    }

    private async Task HandleMessageDeleteAsync(Cacheable<IMessage, ulong> cachedMessage, Cacheable<IMessageChannel, ulong> cachedChannel)
    {
        if (!cachedMessage.HasValue) return;
        if (cachedMessage.Value.Author.IsBot || cachedMessage.Value.Author.IsWebhook) return;

        var channel = await cachedChannel.GetOrDownloadAsync();
        var message = cachedMessage.Value;

        if (channel is not IGuildChannel guildChannel || guildChannel.Guild.Id != GuildID) return;

        var sb = new StringBuilder()
            .AppendLine($"### Message deleted in <#{channel.Id}>")
            .AppendLine($"```{message.Content}```");

        await SendMessageAsync(message.Author, sb.ToString());
    }

    private async Task HandleMemberNicknameChangeAsync(Cacheable<SocketGuildUser, ulong> before, SocketGuildUser after)
    {
        if (after.IsBot || after.IsWebhook || after.Guild.Id != GuildID) return;

        var beforeUser = before.Value;
        if (beforeUser == null || beforeUser.Nickname == after.Nickname) return;

        var sb = new StringBuilder()
            .AppendLine($"### Nickname change")
            .AppendLine($"- `@{beforeUser.DisplayName}` **->** `@{after.DisplayName}`");

        await SendMessageAsync(after, sb.ToString());
    }

    private async Task HandleUsernameChangeAsync(SocketUser before, SocketUser after)
    {
        if (after.IsBot || after.IsWebhook || after is not SocketGuildUser guildUser || guildUser.Guild.Id != GuildID) return;

        var sb = new StringBuilder()
            .AppendLine($"### Username change")
            .AppendLine($"- `@{before.Username}` **->** `@{after.Username}`");

        await SendMessageAsync(after, sb.ToString());
    }

    private async Task HandlePresenceUpdateAsync(SocketUser user, SocketPresence before, SocketPresence after)
    {
        if (user.IsBot || user.IsWebhook || user is not SocketGuildUser guildUser || guildUser.Guild.Id != GuildID) return;

        var sb = new StringBuilder();

        if (!CompareClient(before.ActiveClients, after.ActiveClients))
            sb.AppendLine($"### Clients change")
              .AppendLine($"- `{GetClientString(before.ActiveClients)}` **->** `{GetClientString(after.ActiveClients)}`");

        if (before.Status == UserStatus.Offline || after.Status == UserStatus.Offline)
            await _mongoDbService.UpdateUserLastSeen(user.Id);

        if (before.Status != after.Status)
            sb.AppendLine($"### Status change")
              .AppendLine($"- `{before.Status}` **->** `{after.Status}`");

        if (sb.Length > 0)
            await SendMessageAsync(user, sb.ToString());
    }

    private async Task HandleVoiceStateChangeAsync(SocketUser user, SocketVoiceState before, SocketVoiceState after)
    {
        if (user.IsBot || user.IsWebhook || user is not SocketGuildUser guildUser || guildUser.Guild.Id != GuildID) return;

        var sb = new StringBuilder();

        if (before.VoiceChannel == null && after.VoiceChannel != null)
            sb.AppendLine($"### Joined VC")
              .AppendLine($"- {after.VoiceChannel.Mention}");
        else if (before.VoiceChannel != null && after.VoiceChannel == null)
            sb.AppendLine($"### Left VC")
              .AppendLine($"- {before.VoiceChannel.Mention}");
        else if (before.VoiceChannel != null && after.VoiceChannel != null && before.VoiceChannel != after.VoiceChannel)
            sb.AppendLine($"### Switched VC")
              .AppendLine($"- {before.VoiceChannel.Mention} **->** {after.VoiceChannel.Mention}");

        if (sb.Length > 0)
            await SendMessageAsync(user, sb.ToString());
    }

    private async Task HandleUserBannedAsync(SocketUser user, SocketGuild guild)
    {
        if (user.IsBot || user.IsWebhook || guild.Id != GuildID) return;

        var sb = new StringBuilder()
            .AppendLine($"### User banned")
            .AppendLine($"- `@{user.Username}` | {user.Mention}")
            .AppendLine($"- **User ID**: `{user.Id}`")
            .AppendLine($"- **Clients**: `{GetClientString(user.ActiveClients)}`")
            .AppendLine($"- **Status**: `{user.Status}`");

        await SendMessageAsync(user, sb.ToString());
    }

    private async Task HandleUserLeftAsync(SocketGuild guild, SocketUser user)
    {
        if (user.IsBot || user.IsWebhook || guild.Id != GuildID) return;

        var sb = new StringBuilder()
            .AppendLine($"### User left guild")
            .AppendLine($"- `@{user.Username}` | {user.Mention}")
            .AppendLine($"- **User ID**: `{user.Id}`")
            .AppendLine($"- **Clients**: `{GetClientString(user.ActiveClients)}`")
            .AppendLine($"- **Status**: `{user.Status}`");

        await SendMessageAsync(user, sb.ToString());
    }

    private async Task HandleUserJoinedAsync(SocketGuildUser user)
    {
        if (user.IsBot || user.IsWebhook || user.Guild.Id != GuildID) return;

        var sb = new StringBuilder()
            .AppendLine($"### User joined guild")
            .AppendLine($"- `@{user.Username}` | {user.Mention}")
            .AppendLine($"- **User ID**: `{user.Id}`")
            .AppendLine($"- **Clients**: `{GetClientString(user.ActiveClients)}`")
            .AppendLine($"- **Status**: `{user.Status}`");

        await SendMessageAsync(user, sb.ToString());
    }

    private async Task HandleUserUnbannedAsync(SocketUser user, SocketGuild guild)
    {
        if (user.IsBot || user.IsWebhook || guild.Id != GuildID) return;

        var sb = new StringBuilder()
            .AppendLine($"### User unbanned")
            .AppendLine($"- `@{user.Username}` | {user.Mention}")
            .AppendLine($"- **User ID**: `{user.Id}`")
            .AppendLine($"- **Clients**: `{GetClientString(user.ActiveClients)}`")
            .AppendLine($"- **Status**: `{user.Status}`");

        await SendMessageAsync(user, sb.ToString());
    }

    private static bool CompareClient(IReadOnlyCollection<ClientType>? before, IReadOnlyCollection<ClientType>? after)
    {
        if (before == null && after == null) return true;
        if (before == null || after == null) return false;
        return before.SequenceEqual(after);
    }

    private static string GetClientString(IReadOnlyCollection<ClientType>? clients)
    {
        if (clients == null || clients.Count == 0) return "Offline";

        return string.Join(", ", clients.Select(client => client switch
        {
            ClientType.Desktop => "Desktop",
            ClientType.Mobile => "Mobile",
            ClientType.Web => "Web",
            _ => "Unknown"
        }));
    }
}
