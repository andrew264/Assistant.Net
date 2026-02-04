using Assistant.Net.Services.Data;
using Assistant.Net.Utilities;
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
    private const string StatusPrefix = "▶️ ";
    private static readonly TimeSpan HistoryRecordThreshold = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StatusDebounceInterval = TimeSpan.FromSeconds(10);

    private readonly MusicHistoryService _historyService =
        properties.ServiceProvider!.GetRequiredService<MusicHistoryService>();

    private readonly ILogger<CustomPlayer> _logger = properties.Logger;
    private readonly DiscordSocketClient _socketClient = properties.Options.Value.SocketClient;
    private DateTimeOffset _lastStatusUpdate = DateTimeOffset.MinValue;

    private CancellationTokenSource? _statusUpdateCts;

    public ValueTask NotifyPlayerActiveAsync(PlayerTrackingState trackingState,
        CancellationToken cancellationToken = default)
        => default;

    public ValueTask NotifyPlayerTrackedAsync(PlayerTrackingState trackingState,
        CancellationToken cancellationToken = default)
        => default;

    public async ValueTask NotifyPlayerInactiveAsync(PlayerTrackingState trackingState,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Player:{GuildId}] Player is inactive. Disconnecting.", GuildId);
        await DisconnectAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async ValueTask NotifyTrackStartedAsync(ITrackQueueItem queueItem,
        CancellationToken cancellationToken = default)
    {
        await base.NotifyTrackStartedAsync(queueItem, cancellationToken).ConfigureAwait(false);

        if (queueItem.Track != null && queueItem.Track.Duration >= HistoryRecordThreshold)
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(HistoryRecordThreshold, CancellationToken.None).ConfigureAwait(false);
                    if (Equals(CurrentItem, queueItem) && State == PlayerState.Playing)
                        await _historyService.AddSongToHistoryAsync(GuildId, queueItem).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error recording history for Guild {GuildId}", GuildId);
                }
            }, CancellationToken.None);

        _logger.LogInformation("[Player:{GuildId}] Track started: {TrackTitle} ({TrackUri})",
            GuildId, queueItem.Track?.Title, queueItem.Track?.Uri);

        if (queueItem.Track is null) return;

        var rawTitle = queueItem.Track.Title;
        var cleanTitle = rawTitle.RemoveStuffInBrackets().Trim().Truncate(128);
        var statusText = $"{StatusPrefix}{cleanTitle}";

        await SetVoiceChannelStatusAsync(statusText, false).ConfigureAwait(false);
    }

    protected override async ValueTask NotifyTrackEndedAsync(ITrackQueueItem queueItem, TrackEndReason endReason,
        CancellationToken cancellationToken = default)
    {
        await base.NotifyTrackEndedAsync(queueItem, endReason, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("[Player:{GuildId}] Track ended: {TrackTitle} ({TrackUri}). Reason: {Reason}",
            GuildId, queueItem.Track?.Title, queueItem.Track?.Uri, endReason);

        if (Queue.IsEmpty)
        {
            _logger.LogTrace("Resetting VC Status for {GuildId} as queue ended.", GuildId);
            await SetVoiceChannelStatusAsync(string.Empty, true).ConfigureAwait(false);
        }
    }

    private async ValueTask SetVoiceChannelStatusAsync(string status, bool isResetting)
    {
        if (VoiceState.VoiceChannelId is not { } voiceChannelId) return;
        var guild = _socketClient.GetGuild(GuildId);
        var voiceChannel = guild?.GetVoiceChannel(voiceChannelId);
        if (voiceChannel is null) return;

        var currentStatus = voiceChannel.Status ?? string.Empty;
        if (!string.IsNullOrEmpty(currentStatus) && !currentStatus.StartsWith(StatusPrefix) && !isResetting) return;

        if (_statusUpdateCts != null)
        {
            await _statusUpdateCts.CancelAsync().ConfigureAwait(false);
            _statusUpdateCts.Dispose();
        }

        _statusUpdateCts = new CancellationTokenSource();
        var token = _statusUpdateCts.Token;

        var now = DateTimeOffset.UtcNow;
        var timeSinceLast = now - _lastStatusUpdate;
        var delay = StatusDebounceInterval - timeSinceLast;

        if (delay <= TimeSpan.Zero || isResetting)
            await PerformStatusUpdateAsync(voiceChannel, status, isResetting).ConfigureAwait(false);
        else
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(delay, token).ConfigureAwait(false);
                    if (token.IsCancellationRequested) return;
                    await PerformStatusUpdateAsync(voiceChannel, status, isResetting).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }, token);
    }

    private async Task PerformStatusUpdateAsync(SocketVoiceChannel voiceChannel, string status, bool isResetting)
    {
        try
        {
            await voiceChannel.SetStatusAsync(status).ConfigureAwait(false);
            _lastStatusUpdate = DateTimeOffset.UtcNow;
            if (!isResetting) _logger.LogTrace("Updated VC Status in #{VCName}: {Status}", voiceChannel.Name, status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update VC Status in #{VCName}", voiceChannel.Name);
        }
    }
}