using MongoDB.Bson.Serialization.Attributes;

namespace Assistant.Net.Models.Games;

// Represents the compound primary key for GameStatsModel
public readonly struct GameStatsIdKey : IEquatable<GameStatsIdKey>
{
    [BsonElement("gid")] public ulong GuildId { get; init; }

    [BsonElement("uid")] public ulong UserId { get; init; }

    public bool Equals(GameStatsIdKey other) => GuildId == other.GuildId && UserId == other.UserId;

    public override bool Equals(object? obj) => obj is GameStatsIdKey other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(GuildId, UserId);

    public static bool operator ==(GameStatsIdKey left, GameStatsIdKey right) => left.Equals(right);

    public static bool operator !=(GameStatsIdKey left, GameStatsIdKey right) => !left.Equals(right);
}