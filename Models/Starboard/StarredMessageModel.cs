using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Assistant.Net.Models;

public class StarredMessageModel
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    [BsonElement("guildId")] public ulong GuildId { get; set; }

    [BsonElement("originalMessageId")] public ulong OriginalMessageId { get; set; }

    [BsonElement("originalChannelId")] public ulong OriginalChannelId { get; set; }

    [BsonElement("starboardMessageId")]
    [BsonIgnoreIfNull]
    public ulong? StarboardMessageId { get; set; }

    [BsonElement("starrerUserIds")] public List<ulong> StarrerUserIds { get; set; } = new();

    [BsonElement("starCount")] public int StarCount { get; set; } = 0;

    [BsonElement("isPosted")] public bool IsPosted { get; set; } = false;

    [BsonElement("lastUpdated")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}