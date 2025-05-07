using MongoDB.Bson.Serialization.Attributes;

namespace Assistant.Net.Models.Reminder;

public struct ReminderIdKey : IEquatable<ReminderIdKey>
{
    [BsonElement("uid")] public ulong UserId { get; set; }

    [BsonElement("seq")] public int SequenceNumber { get; set; }

    public bool Equals(ReminderIdKey other) => UserId == other.UserId && SequenceNumber == other.SequenceNumber;

    public override bool Equals(object? obj) => obj is ReminderIdKey other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(UserId, SequenceNumber);

    public static bool operator ==(ReminderIdKey left, ReminderIdKey right) => left.Equals(right);

    public static bool operator !=(ReminderIdKey left, ReminderIdKey right) => !left.Equals(right);
}