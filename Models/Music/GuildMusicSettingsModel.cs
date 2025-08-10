using MongoDB.Bson.Serialization.Attributes;

namespace Assistant.Net.Models.Music;

public class GuildMusicSettingsModel
{
    [BsonId] // GuildId
    public ulong GuildId { get; init; }

    [BsonElement("volume")] public float Volume { get; init; }

    [BsonElement("songs")] public List<SongHistoryEntry> Songs { get; init; } = [];

    [BsonElement("updatedAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
}