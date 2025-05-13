using MongoDB.Bson.Serialization.Attributes;

namespace Assistant.Net.Models.Playlist;

public class PlaylistModel
{
    [BsonId] public PlaylistIdKey Id { get; set; }

    [BsonElement("name")] [BsonRequired] public string Name { get; set; } = null!;

    [BsonElement("songs")] public List<SongModel> Songs { get; set; } = new();

    [BsonElement("createdAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}