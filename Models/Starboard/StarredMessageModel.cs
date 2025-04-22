using MongoDB.Bson.Serialization.Attributes;

namespace Assistant.Net.Models.Starboard;

public class StarredMessageModel
{
    [BsonId]
    // composite key structure
    public StarredMessageIdKey Id { get; init; }

    [BsonElement("originalChannelId")] public ulong OriginalChannelId { get; set; }

    [BsonElement("starboardMessageId")]
    [BsonIgnoreIfDefault]
    public ulong? StarboardMessageId { get; set; }

    [BsonElement("starrerUserIds")] public HashSet<ulong> StarrerUserIds { get; set; } = [];

    [BsonElement("starCount")] public int StarCount { get; set; } = 0;

    [BsonElement("isPosted")] public bool IsPosted { get; set; } = false;

    [BsonElement("lastUpdated")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}