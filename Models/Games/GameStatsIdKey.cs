using MongoDB.Bson.Serialization.Attributes;

namespace Assistant.Net.Models.Games;

// Represents the compound primary key for GameStatsModel
public struct GameStatsIdKey : IEquatable<GameStatsIdKey>
{
    [BsonElement("gid")] public ulong GuildId { get; set; }

    [BsonElement("uid")] public ulong UserId { get; set; }

    public bool Equals(GameStatsIdKey other)
    {
        return GuildId == other.GuildId && UserId == other.UserId;
    }

    public override bool Equals(object? obj)
    {
        return obj is GameStatsIdKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(GuildId, UserId);
    }

    public static bool operator ==(GameStatsIdKey left, GameStatsIdKey right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(GameStatsIdKey left, GameStatsIdKey right)
    {
        return !left.Equals(right);
    }
}