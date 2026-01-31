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

    private readonly MusicHistoryService _historyService =
        properties.ServiceProvider!.GetRequiredService<MusicHistoryService>();

    private readonly ILogger<CustomPlayer> _logger = properties.Logger;
    private readonly DiscordSocketClient _socketClient = properties.Options.Value.SocketClient;

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
        await _historyService.AddSongToHistoryAsync(GuildId, queueItem).ConfigureAwait(false);

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
        if (!string.IsNullOrEmpty(currentStatus) && !currentStatus.StartsWith(StatusPrefix)) return;

        try
        {
            await voiceChannel.SetStatusAsync(status).ConfigureAwait(false);
            if (!isResetting) _logger.LogTrace("Updated VC Status in #{VCName}: {Status}", voiceChannel.Name, status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update VC Status in #{VCName}", voiceChannel.Name);
        }
    }
}