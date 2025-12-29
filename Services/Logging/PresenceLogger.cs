using Assistant.Net.Configuration;
using Assistant.Net.Services.Core;
using Assistant.Net.Utilities;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Services.Logging;

public class PresenceLogger
{
    private const int DeleteDelay = 24 * 60 * 60 * 1000;
    private readonly Config _config;
    private readonly ILogger<PresenceLogger> _logger;
    private readonly WebhookService _webhookService;

    public PresenceLogger(
        DiscordSocketClient client,
        Config config,
        ILogger<PresenceLogger> logger,
        WebhookService webhookService)
    {
        _config = config;
        _logger = logger;
        _webhookService = webhookService;

        client.PresenceUpdated += HandlePresenceUpdatedAsync;

        _logger.LogInformation("PresenceLogger initialized.");
    }

    private LoggingGuildConfig? GetLoggingGuildConfig(ulong guildId)
    {
        return _config.LoggingGuilds?.FirstOrDefault(kvp => kvp.Value.GuildId == guildId).Value;
    }

    private async Task HandlePresenceUpdatedAsync(SocketUser user, SocketPresence before, SocketPresence after)
    {
        if (user.IsBot || user is not SocketGuildUser guildUser) return;

        var loggingConfig = GetLoggingGuildConfig(guildUser.Guild.Id);
        if (loggingConfig is not { LogPresenceUpdates: true }) return;

        var bClients = ActivityUtils.GetClients(before);
        var aClients = ActivityUtils.GetClients(after);
        var bStatus = before.Status.ToString().ToLowerInvariant();
        var aStatus = after.Status.ToString().ToLowerInvariant();

        if (bStatus == aStatus &&
            bClients.SetEquals(aClients) &&
            before.Activities.SequenceEqual(after.Activities, ActivityComparer.Instance)) return;


        var webhookClient = await _webhookService.GetOrCreateWebhookClientAsync(loggingConfig.ChannelId)
            .ConfigureAwait(false);
        if (webhookClient == null) return;

        var statusSummary = ActivityUtils.SummarizeStatusChange(bClients, bStatus, aClients, aStatus);
        var logParts = new List<string>();
        if (statusSummary != null) logParts.Add(statusSummary);

        _logger.LogInformation("[UPDATE] Presence @{User} from {GuildName} {Summary}", user.Username,
            guildUser.Guild.Name,
            statusSummary ?? "No direct status/client change.");

        var bActivities = ActivityUtils.GetAllUserActivities(before.Activities, false, true, true);
        var aActivities = ActivityUtils.GetAllUserActivities(after.Activities, false, true, true);
        var allActivityKeys = bActivities.Keys.Union(aActivities.Keys).ToHashSet();

        var activityChanged = false;
        foreach (var key in allActivityKeys.Where(key => key != "Spotify"))
        {
            bActivities.TryGetValue(key, out var bValue);
            aActivities.TryGetValue(key, out var aValue);

            if (bValue == aValue) continue;
            activityChanged = true;

            var changeDescription = "";
            if (key == "Custom Status")
            {
                switch (string.IsNullOrEmpty(bValue))
                {
                    case false when !string.IsNullOrEmpty(aValue):
                        changeDescription = $"Custom Status: `{bValue}` → `{aValue}`";
                        break;
                    case false:
                        changeDescription = $"Removed Custom Status: `{bValue}`";
                        break;
                    default:
                    {
                        if (!string.IsNullOrEmpty(aValue)) changeDescription = $"Set Custom Status: `{aValue}`";
                        break;
                    }
                }
            }
            else
            {
                if (string.IsNullOrEmpty(bValue)) changeDescription = $"Started {key}: {aValue}";
                else if (string.IsNullOrEmpty(aValue)) changeDescription = $"Stopped {key}: {bValue}";
                else changeDescription = $"{key}: `{bValue}` → `{aValue}`";
            }

            if (!string.IsNullOrEmpty(changeDescription)) logParts.Add(changeDescription);
        }

        if (statusSummary == null && !activityChanged) return;

        var messageContent = string.Join("\n", logParts).Trim();
        if (string.IsNullOrEmpty(messageContent)) return;

        try
        {
            var msgId = await webhookClient.SendMessageAsync(
                messageContent.Truncate(DiscordConfig.MaxMessageSize),
                username: guildUser.DisplayName,
                avatarUrl: user.GetDisplayAvatarUrl() ?? user.GetDefaultAvatarUrl(),
                allowedMentions: AllowedMentions.None,
                flags: MessageFlags.SuppressEmbeds
            ).ConfigureAwait(false);
            _ = Task.Delay(DeleteDelay)
                .ContinueWith(_ => webhookClient.DeleteMessageAsync(msgId).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send presence update log via webhook for User {UserId}.", user.Id);
        }
    }

    private class ActivityComparer : IEqualityComparer<IActivity>
    {
        public static readonly ActivityComparer Instance = new();

        public bool Equals(IActivity? x, IActivity? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;
            if (x.Type != y.Type) return false;
            if (x.Name != y.Name) return false;

            return x switch
            {
                CustomStatusGame csgX when y is CustomStatusGame csgY => csgX.State == csgY.State &&
                                                                         csgX.Emote?.Name == csgY.Emote?.Name &&
                                                                         (csgX.Emote as GuildEmote)?.Id ==
                                                                         (csgY.Emote as GuildEmote)?.Id,
                RichGame rgX when y is RichGame rgY => rgX.Details == rgY.Details && rgX.State == rgY.State,
                _ => true
            };
        }

        public int GetHashCode(IActivity obj) => HashCode.Combine(obj.Type, obj.Name);
    }
}