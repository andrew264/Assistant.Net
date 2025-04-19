using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Assistant.Net.Models;

public class ReminderModel
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    [BsonElement("reminderId")] public string ReminderId { get; set; } = Guid.NewGuid().ToString();

    [BsonElement("userId")] public ulong UserId { get; set; }

    [BsonElement("targetUserId")]
    [BsonIgnoreIfNull]
    public ulong? TargetUserId { get; set; }

    [BsonElement("channelId")] public ulong ChannelId { get; set; }

    [BsonElement("guildId")] public ulong GuildId { get; set; }

    [BsonElement("message")] public string Message { get; set; } = null!;

    [BsonElement("triggerTime")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime TriggerTime { get; set; }

    [BsonElement("creationTime")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreationTime { get; set; } = DateTime.UtcNow;

    [BsonElement("isDm")] public bool IsDm { get; set; } = true;

    [BsonElement("title")]
    [BsonIgnoreIfNull]
    public string? Title { get; set; }

    [BsonElement("recurrence")]
    [BsonIgnoreIfNull]
    public string? Recurrence { get; set; }

    [BsonElement("lastTriggered")]
    [BsonIgnoreIfNull]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? LastTriggered { get; set; }

    [BsonElement("isActive")] public bool IsActive { get; set; } = true;
}