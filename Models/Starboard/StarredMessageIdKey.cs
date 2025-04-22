using MongoDB.Bson.Serialization.Attributes;

namespace Assistant.Net.Models.Starboard;

public struct StarredMessageIdKey
{
    [BsonElement("gid")] public ulong GuildId { get; set; }

    [BsonElement("mid")] public ulong OriginalMessageId { get; set; }

    // Optional: Implement IEquatable<StarredMessageIdKey> and override GetHashCode/Equals
    // for better performance in dictionaries/hashsets if needed elsewhere,
    // though MongoDB driver handles comparisons correctly.
}