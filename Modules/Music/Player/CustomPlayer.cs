using Assistant.Net.Configuration;
using Assistant.Net.Models.Music;
using Assistant.Net.Services.Music;
using Assistant.Net.Utilities;
using Discord;
using Discord.WebSocket;
using Lavalink4NET.InactivityTracking.Players;
using Lavalink4NET.InactivityTracking.Trackers;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.Protocol.Payloads.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Music.Player;

public sealed class CustomPlayer(IPlayerProperties<CustomPlayer, CustomPlayerOptions> properties)
    : QueuedLavalinkPlayer(properties), IInactivityPlayerListener
{
    private readonly MusicHistoryService _historyService =
        properties.ServiceProvider!.GetRequiredService<MusicHistoryService>();

    private readonly ILogger<CustomPlayer> _logger =
        properties.ServiceProvider!.GetRequiredService<ILogger<CustomPlayer>>();

    private DiscordSocketClient SocketClient => properties.Options.Value.SocketClient;
    private Config AppConfig => properties.Options.Value.ApplicationConfig;
    private bool IsHomeGuildPlayer => GuildId == AppConfig.Client.HomeGuildId;

    public ValueTask NotifyPlayerActiveAsync(PlayerTrackingState trackingState,
        CancellationToken cancellationToken = default) => default;

    public async ValueTask NotifyPlayerInactiveAsync(PlayerTrackingState trackingState,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Player:{GuildId}] Player is inactive. Disconnecting.", GuildId);
        await DisconnectAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask NotifyPlayerTrackedAsync(PlayerTrackingState trackingState,
        CancellationToken cancellationToken = default) => default;

    // Add track to history when started
    private async Task AddTrackToHistoryAsync(ITrackQueueItem queueItem)
    {
        if (queueItem.Track?.Uri is null)
        {
            _logger.LogWarning("Cannot add track to history: Track or URI is null. Guild: {GuildId}", GuildId);
            return;
        }

        var historyEntry = new SongHistoryEntry
        {
            Title = queueItem.Track.Title,
            Uri = queueItem.Track.Uri.ToString(),
            PlayedAt = DateTime.UtcNow,
            PlayedBy = 0, // TODO: Need a better way to track requester
            Duration = queueItem.Track.Duration,
            ThumbnailUrl = queueItem.Track.ArtworkUri?.ToString(),
            Artist = queueItem.Track.Author
        };

        await _historyService.AddSongToHistoryAsync(GuildId, historyEntry).ConfigureAwait(false);
    }

    protected override async ValueTask NotifyTrackStartedAsync(ITrackQueueItem queueItem,
        CancellationToken cancellationToken = default)
    {
        await base.NotifyTrackStartedAsync(queueItem, cancellationToken).ConfigureAwait(false);

        // Update bot presence if in home guild
        if (IsHomeGuildPlayer)
        {
            var title = queueItem.Track?.Title ?? "Unknown Track";
            var cleanTitle = title.RemoveStuffInBrackets().Trim().Truncate(128);

            try
            {
                await SocketClient.SetActivityAsync(new Game(cleanTitle, ActivityType.Listening)).ConfigureAwait(false);
                _logger.LogDebug("Updated bot presence for Home Guild {GuildId}: Listening to {TrackTitle}", GuildId,
                    cleanTitle);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update bot presence for Home Guild {GuildId}", GuildId);
            }
        }

        // Add to history
        await AddTrackToHistoryAsync(queueItem).ConfigureAwait(false);

        _logger.LogInformation("[Player:{GuildId}] Track started: {TrackTitle} ({TrackUri})", GuildId,
            queueItem.Track?.Title, queueItem.Track?.Uri);
    }

    protected override async ValueTask NotifyTrackEndedAsync(ITrackQueueItem queueItem, TrackEndReason endReason,
        CancellationToken cancellationToken = default)
    {
        await base.NotifyTrackEndedAsync(queueItem, endReason, cancellationToken).ConfigureAwait(false);

        // Reset bot presence if in home guild and queue is empty
        if (IsHomeGuildPlayer && Queue.IsEmpty)
            try
            {
                var activityText = AppConfig.Client.ActivityText;
                var activityType = Enum.TryParse<ActivityType>(AppConfig.Client.ActivityType, true, out var type)
                    ? type
                    : ActivityType.Playing;

                if (!string.IsNullOrEmpty(activityText))
                    await SocketClient.SetActivityAsync(new Game(activityText, activityType)).ConfigureAwait(false);
                else
                    await SocketClient.SetActivityAsync(null).ConfigureAwait(false);
                _logger.LogDebug("Reset bot presence for Home Guild {GuildId} as queue ended.", GuildId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reset bot presence for Home Guild {GuildId}", GuildId);
            }

        _logger.LogInformation("[Player:{GuildId}] Track ended: {TrackTitle} ({TrackUri}). Reason: {Reason}", GuildId,
            queueItem.Track?.Title, queueItem.Track?.Uri, endReason);
    }
}