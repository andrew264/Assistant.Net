using Lavalink4NET.Players.Queued;
using Lavalink4NET.Tracks;

namespace Assistant.Net.Models.Music;

public sealed record CustomTrackQueueItem : TrackQueueItem
{
    public CustomTrackQueueItem(LavalinkTrack track, ulong requesterId) : base(track)
    {
        RequesterId = requesterId;
    }

    public ulong RequesterId { get; }
}