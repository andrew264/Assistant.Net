using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Assistant.Net.Models;

public class PlaylistModel
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    [BsonElement("userId")] public ulong UserId { get; set; }

    [BsonElement("guildId")] public ulong GuildId { get; set; }

    [BsonElement("name")] [BsonRequired] public string Name { get; set; } = null!;

    [BsonElement("songs")] public List<SongModel> Songs { get; set; } = new();

    [BsonElement("createdAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}