using Assistant.Net.Options;
using Assistant.Net.Services.Data;
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

namespace Assistant.Net.Services.Music.Logic;

public sealed class CustomPlayer(IPlayerProperties<CustomPlayer, CustomPlayerOptions> properties)
    : QueuedLavalinkPlayer(properties), IInactivityPlayerListener
{
    private readonly MusicHistoryService _historyService =
        properties.ServiceProvider!.GetRequiredService<MusicHistoryService>();

    private readonly ILogger<CustomPlayer> _logger =
        properties.ServiceProvider!.GetRequiredService<ILogger<CustomPlayer>>();

    private DiscordSocketClient SocketClient => properties.Options.Value.SocketClient;
    private DiscordOptions DiscordOptions => properties.Options.Value.DiscordOptions;
    private bool IsHomeGuildPlayer => GuildId == DiscordOptions.HomeGuildId;

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

    protected override async ValueTask NotifyTrackStartedAsync(ITrackQueueItem queueItem,
        CancellationToken cancellationToken = default)
    {
        await base.NotifyTrackStartedAsync(queueItem, cancellationToken).ConfigureAwait(false);

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

        await _historyService.AddSongToHistoryAsync(GuildId, queueItem).ConfigureAwait(false);

        _logger.LogInformation("[Player:{GuildId}] Track started: {TrackTitle} ({TrackUri})", GuildId,
            queueItem.Track?.Title, queueItem.Track?.Uri);
    }

    protected override async ValueTask NotifyTrackEndedAsync(ITrackQueueItem queueItem, TrackEndReason endReason,
        CancellationToken cancellationToken = default)
    {
        await base.NotifyTrackEndedAsync(queueItem, endReason, cancellationToken).ConfigureAwait(false);

        if (IsHomeGuildPlayer && Queue.IsEmpty)
            try
            {
                var activityText = DiscordOptions.ActivityText;
                var activityType = Enum.TryParse<ActivityType>(DiscordOptions.ActivityType, true, out var type)
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