using MongoDB.Bson.Serialization.Attributes;

namespace Assistant.Net.Models.Playlist;

// Represents the compound primary key for PlaylistModel
public readonly struct PlaylistIdKey : IEquatable<PlaylistIdKey>
{
    [BsonElement("uid")] public ulong UserId { get; init; }

    [BsonElement("gid")] public ulong GuildId { get; init; }

    public bool Equals(PlaylistIdKey other) => UserId == other.UserId && GuildId == other.GuildId;

    public override bool Equals(object? obj) => obj is PlaylistIdKey other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(UserId, GuildId);

    public static bool operator ==(PlaylistIdKey left, PlaylistIdKey right) => left.Equals(right);

    public static bool operator !=(PlaylistIdKey left, PlaylistIdKey right) => !left.Equals(right);
}