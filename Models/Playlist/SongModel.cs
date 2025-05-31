using MongoDB.Bson.Serialization.Attributes;

namespace Assistant.Net.Models.Playlist;

public class SongModel
{
    [BsonElement("title")] public string Title { get; set; } = null!;

    [BsonElement("artist")] public string Artist { get; set; } = null!;

    [BsonElement("uri")] public string Uri { get; set; } = null!;

    [BsonElement("duration")] public double Duration { get; set; }

    [BsonElement("thumbnail")]
    [BsonIgnoreIfNull]
    public string? Thumbnail { get; set; }

    [BsonElement("source")] public string Source { get; set; } = "other";
}