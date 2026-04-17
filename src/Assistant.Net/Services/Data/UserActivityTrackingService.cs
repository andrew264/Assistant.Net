using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Services.Data;

public class UserActivityTrackingService(
    DiscordSocketClient client,
    UserService userService,
    ILogger<UserActivityTrackingService> logger)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        client.PresenceUpdated += OnPresenceUpdatedAsync;
        client.MessageReceived += OnMessageReceivedAsync;
        client.UserVoiceStateUpdated += OnVoiceStateUpdatedAsync;
        client.UserJoined += OnUserJoinedAsync;
        client.UserIsTyping += OnTypingStartedAsync;

        logger.LogInformation("UserActivityTrackingService started and events hooked.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        client.PresenceUpdated -= OnPresenceUpdatedAsync;
        client.MessageReceived -= OnMessageReceivedAsync;
        client.UserVoiceStateUpdated -= OnVoiceStateUpdatedAsync;
        client.UserJoined -= OnUserJoinedAsync;
        client.UserIsTyping -= OnTypingStartedAsync;

        logger.LogInformation("UserActivityTrackingService stopped and events unhooked.");
        return Task.CompletedTask;
    }

    private Task OnPresenceUpdatedAsync(SocketUser user, SocketPresence before, SocketPresence after)
    {
        return Task.Run(async () =>
        {
            if (user.IsBot) return;

            var beforeStatus = before.Status;
            var afterStatus = after.Status;

            var transitioned = (beforeStatus == UserStatus.Offline && afterStatus != UserStatus.Offline) ||
                               (beforeStatus != UserStatus.Offline && afterStatus == UserStatus.Offline);

            if (transitioned)
            {
                logger.LogTrace("Presence transition detected for {User} ({Before} -> {After}). Updating LastSeen.",
                    user.Username, beforeStatus, afterStatus);
                await UpdateUserLastSeen(user.Id, "PresenceUpdate").ConfigureAwait(false);
            }
        });
    }


    private Task OnMessageReceivedAsync(SocketMessage message)
    {
        return Task.Run(async () =>
        {
            if (message is not SocketUserMessage { Source: MessageSource.User } userMessage ||
                userMessage.Author.IsBot ||
                userMessage.Channel is IDMChannel ||
                userMessage.Author.Status != UserStatus.Offline) return;

            logger.LogTrace("Message received from {User} in guild channel. Updating LastSeen.",
                userMessage.Author.Username);
            await UpdateUserLastSeen(userMessage.Author.Id, "MessageReceived").ConfigureAwait(false);
        });
    }


    private Task OnTypingStartedAsync(Cacheable<IUser, ulong> userCacheable,
        Cacheable<IMessageChannel, ulong> channelCacheable)
    {
        return Task.Run(async () =>
        {
            if (!channelCacheable.HasValue || channelCacheable.Value is IDMChannel) return;
            if (!userCacheable.HasValue || userCacheable.Value.IsBot) return;

            var user = userCacheable.Value;
            if (user.Status != UserStatus.Offline) return;

            logger.LogTrace("Typing started by {User}(offline) in guild channel. Updating LastSeen.", user.Username);
            await UpdateUserLastSeen(user.Id, "TypingStarted").ConfigureAwait(false);
        });
    }

    private Task OnVoiceStateUpdatedAsync(SocketUser user, SocketVoiceState before, SocketVoiceState after)
    {
        return Task.Run(async () =>
        {
            if (user.IsBot || user is not SocketGuildUser || user.Status != UserStatus.Offline) return;

            logger.LogTrace("Voice state updated for {User}(offline). Updating LastSeen.", user.Username);
            await UpdateUserLastSeen(user.Id, "VoiceStateUpdate").ConfigureAwait(false);
        });
    }

    private Task OnUserJoinedAsync(SocketGuildUser user)
    {
        return Task.Run(async () =>
        {
            if (user.IsBot) return;

            logger.LogTrace("User {User} joined guild. Updating LastSeen.", user.Username);
            await UpdateUserLastSeen(user.Id, "UserJoined").ConfigureAwait(false);
        });
    }

    private async Task UpdateUserLastSeen(ulong userId, string eventSource)
    {
        try
        {
            await userService.UpdateLastSeenAsync(userId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update LastSeen for User {UserId} triggered by {EventSource}.", userId,
                eventSource);
        }
    }
}