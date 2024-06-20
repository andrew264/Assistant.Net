using Discord;
using Discord.WebSocket;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.Protocol.Payloads.Events;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace Assistant.Net.Modules.Music;

public sealed partial class CustomPlayer(IPlayerProperties<CustomPlayer, CustomPlayerOptions> properties) : QueuedLavalinkPlayer(properties)
{
    public CustomPlayerOptions playerOptions => properties.Options.Value;
    public DiscordSocketClient? discordSocketClient => properties.Options.Value.DiscordClient;
    protected override async ValueTask NotifyTrackEndedAsync(ITrackQueueItem queueItem, TrackEndReason endReason, CancellationToken cancellationToken = default)
    {
        await base.NotifyTrackEndedAsync(queueItem, endReason, cancellationToken).ConfigureAwait(false);

        if (playerOptions.IsHomeGuild && discordSocketClient != null && this.Queue.Count == 0 && this.CurrentTrack == null)
        {
            var (text, type) = playerOptions.DefaultActivity;
            await discordSocketClient.SetGameAsync(text, type: type).ConfigureAwait(false);
        }
    }

    protected override async ValueTask NotifyTrackStartedAsync(ITrackQueueItem queueItem, CancellationToken cancellationToken = default)
    {
        await base.NotifyTrackStartedAsync(queueItem, cancellationToken).ConfigureAwait(false);

        if (playerOptions.IsHomeGuild && discordSocketClient != null)
        {
            var title = RemoveBrackets(queueItem.Track!.Title);
            await discordSocketClient.SetGameAsync(title, type: ActivityType.Listening).ConfigureAwait(false);
        }
    }

    public static string RemoveBrackets(string input)
    {
        return RemoveBracket().Replace(input, "").Trim();
    }

    [GeneratedRegex(@"[\(\[].*?[\)\]]")]
    private static partial Regex RemoveBracket();
}

public sealed record class CustomPlayerOptions : QueuedLavalinkPlayerOptions, IOptions<CustomPlayerOptions>
{
    public bool IsHomeGuild { get; init; } = false;
    public DiscordSocketClient? DiscordClient { get; init; } = null;
    public (string, ActivityType) DefaultActivity { get; init; } = ("", ActivityType.Playing);
    public CustomPlayerOptions Value => this;
}
