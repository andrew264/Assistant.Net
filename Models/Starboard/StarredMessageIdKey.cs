using MongoDB.Bson.Serialization.Attributes;

namespace Assistant.Net.Models.Starboard;

public readonly struct StarredMessageIdKey : IEquatable<StarredMessageIdKey>
{
    [BsonElement("gid")] public ulong GuildId { get; init; }

    [BsonElement("mid")] public ulong OriginalMessageId { get; init; }

    public bool Equals(StarredMessageIdKey other) =>
        GuildId == other.GuildId && OriginalMessageId == other.OriginalMessageId;

    public override bool Equals(object? obj) => obj is StarredMessageIdKey other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(GuildId, OriginalMessageId);

    public static bool operator ==(StarredMessageIdKey left, StarredMessageIdKey right) => left.Equals(right);

    public static bool operator !=(StarredMessageIdKey left, StarredMessageIdKey right) => !left.Equals(right);
}