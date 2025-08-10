using MongoDB.Bson.Serialization.Attributes;

namespace Assistant.Net.Models.Reminder;

public class ReminderModel
{
    [BsonId] public ReminderIdKey Id { get; init; }

    [BsonElement("targetUserId")]
    [BsonIgnoreIfNull]
    public ulong? TargetUserId { get; init; }

    [BsonElement("channelId")] public ulong ChannelId { get; init; }

    [BsonElement("guildId")] public ulong GuildId { get; set; }

    [BsonElement("message")] public string Message { get; init; } = null!;

    [BsonElement("triggerTime")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime TriggerTime { get; init; }

    [BsonElement("creationTime")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreationTime { get; init; } = DateTime.UtcNow;

    [BsonElement("isDm")] public bool IsDm { get; init; } = true;

    [BsonElement("title")]
    [BsonIgnoreIfNull]
    public string? Title { get; init; }

    [BsonElement("recurrence")]
    [BsonIgnoreIfNull]
    public string? Recurrence { get; init; }

    [BsonElement("lastTriggered")]
    [BsonIgnoreIfNull]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? LastTriggered { get; init; }

    [BsonElement("isActive")] public bool IsActive { get; init; } = true;
}