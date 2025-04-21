using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Services;

public class UserActivityTrackingService
{
    private readonly DiscordSocketClient _client;
    private readonly ILogger<UserActivityTrackingService> _logger;
    private readonly UserService _userService;

    public UserActivityTrackingService(
        DiscordSocketClient client,
        UserService userService,
        ILogger<UserActivityTrackingService> logger)
    {
        _client = client;
        _userService = userService;
        _logger = logger;

        _client.PresenceUpdated += OnPresenceUpdatedAsync;
        _client.MessageReceived += OnMessageReceivedAsync;
        _client.UserVoiceStateUpdated += OnVoiceStateUpdatedAsync;
        _client.UserJoined += OnUserJoinedAsync;
        _client.UserIsTyping += OnTypingStartedAsync;

        _logger.LogInformation("UserActivityTrackingService initialized and events hooked.");
    }


    private async Task OnPresenceUpdatedAsync(SocketUser user, SocketPresence before, SocketPresence after)
    {
        if (user.IsBot) return;

        var beforeStatus = before.Status;
        var afterStatus = after.Status;

        var transitioned = (beforeStatus == UserStatus.Offline && afterStatus != UserStatus.Offline) ||
                           (beforeStatus != UserStatus.Offline && afterStatus == UserStatus.Offline);

        if (transitioned)
        {
            _logger.LogTrace("Presence transition detected for {User} ({Before} -> {After}). Updating LastSeen.",
                user.Username, beforeStatus, afterStatus);
            await UpdateUserLastSeen(user.Id, "PresenceUpdate");
        }
    }


    private async Task OnMessageReceivedAsync(SocketMessage message)
    {
        if (message is not SocketUserMessage { Source: MessageSource.User } userMessage ||
            userMessage.Author.IsBot ||
            userMessage.Channel is IDMChannel ||
            userMessage.Author.Status != UserStatus.Offline) return;

        _logger.LogTrace("Message received from {User} in guild channel. Updating LastSeen.",
            userMessage.Author.Username);
        await UpdateUserLastSeen(userMessage.Author.Id, "MessageReceived");
    }


    private async Task OnTypingStartedAsync(Cacheable<IUser, ulong> userCacheable,
        Cacheable<IMessageChannel, ulong> channelCacheable)
    {
        if (!channelCacheable.HasValue || channelCacheable.Value is IDMChannel) return;
        if (!userCacheable.HasValue || userCacheable.Value.IsBot) return;

        var user = userCacheable.Value;
        if (user.Status != UserStatus.Offline) return;

        _logger.LogTrace("Typing started by {User}(offline) in guild channel. Updating LastSeen.", user.Username);
        await UpdateUserLastSeen(user.Id, "TypingStarted");
    }

    private async Task OnVoiceStateUpdatedAsync(SocketUser user, SocketVoiceState before, SocketVoiceState after)
    {
        if (user.IsBot || user is not SocketGuildUser || user.Status != UserStatus.Offline) return;

        _logger.LogTrace("Voice state updated for {User}(offline). Updating LastSeen.", user.Username);
        await UpdateUserLastSeen(user.Id, "VoiceStateUpdate");
    }

    private async Task OnUserJoinedAsync(SocketGuildUser user)
    {
        if (user.IsBot) return;

        _logger.LogTrace("User {User} joined guild. Updating LastSeen.", user.Username);
        await UpdateUserLastSeen(user.Id, "UserJoined");
    }

    private async Task UpdateUserLastSeen(ulong userId, string eventSource)
    {
        try
        {
            await _userService.UpdateLastSeenAsync(userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update LastSeen for User {UserId} triggered by {EventSource}.", userId,
                eventSource);
        }
    }
}