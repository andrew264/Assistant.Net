using MongoDB.Bson.Serialization.Attributes;

namespace Assistant.Net.Models.Starboard;

public class StarboardConfigModel
{
    [BsonId] public ulong GuildId { get; init; }

    [BsonElement("isEnabled")] public bool IsEnabled { get; set; }

    [BsonElement("starboardChannelId")]
    [BsonIgnoreIfNull]
    public ulong? StarboardChannelId { get; set; }

    [BsonElement("starEmoji")] public string StarEmoji { get; set; } = "⭐";

    [BsonElement("threshold")] public int Threshold { get; set; } = 3;

    [BsonElement("allowSelfStar")] public bool AllowSelfStar { get; set; }

    [BsonElement("allowBotMessages")] public bool AllowBotMessages { get; set; }

    [BsonElement("ignoreNsfwChannels")] public bool IgnoreNsfwChannels { get; set; } = true;

    [BsonElement("deleteIfUnStarred")] public bool DeleteIfUnStarred { get; set; }

    [BsonElement("logChannelId")]
    [BsonIgnoreIfDefault]
    public ulong? LogChannelId { get; set; }

    [BsonElement("createdAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}