using MongoDB.Bson.Serialization.Attributes;

namespace Assistant.Net.Models.Music;

public class GuildMusicSettingsModel
{
    [BsonId] // GuildId
    public ulong GuildId { get; set; }

    [BsonElement("volume")] public float Volume { get; set; }

    [BsonElement("songs")] public List<SongHistoryEntry> Songs { get; set; } = new();

    [BsonElement("updatedAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}