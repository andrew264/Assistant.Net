using MongoDB.Bson.Serialization.Attributes;

namespace Assistant.Net.Models.Playlist;

public class SongModel
{
    [BsonElement("title")] public string Title { get; init; } = null!;

    [BsonElement("artist")] public string Artist { get; init; } = null!;

    [BsonElement("uri")] public string Uri { get; init; } = null!;

    [BsonElement("duration")] public double Duration { get; init; }

    [BsonElement("thumbnail")]
    [BsonIgnoreIfNull]
    public string? Thumbnail { get; init; }

    [BsonElement("source")] public string Source { get; init; } = "other";
}