using MongoDB.Bson.Serialization.Attributes;

namespace Assistant.Net.Models.User;

public class UserModel
{
    [BsonId] public ulong UserId { get; set; }

    [BsonElement("about")]
    [BsonIgnoreIfNull]
    public string? About { get; set; }

    [BsonElement("lastSeen")]
    [BsonIgnoreIfNull]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? LastSeen { get; set; }
}