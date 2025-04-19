using MongoDB.Bson.Serialization.Attributes;

namespace Assistant.Net.Models;

public class StarboardConfigModel
{
    [BsonId] public ulong GuildId { get; set; }

    [BsonElement("isEnabled")] public bool IsEnabled { get; set; } = false;

    [BsonElement("starboardChannelId")]
    [BsonIgnoreIfNull]
    public ulong? StarboardChannelId { get; set; }

    [BsonElement("starEmoji")] public string StarEmoji { get; set; } = "⭐";

    [BsonElement("threshold")] public int Threshold { get; set; } = 3;

    [BsonElement("allowSelfStar")] public bool AllowSelfStar { get; set; } = false;

    [BsonElement("allowBotMessages")] public bool AllowBotMessages { get; set; } = false;

    [BsonElement("ignoreNsfwChannels")] public bool IgnoreNsfwChannels { get; set; } = true;

    [BsonElement("deleteIfUnStarred")] public bool DeleteIfUnStarred { get; set; } = false;

    [BsonElement("logChannelId")]
    [BsonIgnoreIfNull]
    public ulong? LogChannelId { get; set; }

    [BsonElement("createdAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}