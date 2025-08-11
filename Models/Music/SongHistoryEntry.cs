using MongoDB.Bson.Serialization.Attributes;

namespace Assistant.Net.Models.Music;

public class SongHistoryEntry
{
    public string Title { get; init; } = null!;
    public string Uri { get; init; } = null!;
    public DateTime PlayedAt { get; set; }

    [BsonElement("PlayedBy")] public ulong RequestedBy { get; set; }

    public TimeSpan Duration { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? Artist { get; set; }
}