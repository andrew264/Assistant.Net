using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Options;

namespace Assistant.Net.Models.Games;

public class GameStatsModel
{
    // Composite ID handled by the service using a filter, no single BsonId needed here for the document root.
    // MongoDB driver will automatically create a default _id if not specified,
    // but we will query/update using the composite key { UserId, GuildId }.

    [BsonElement("user_id")] public ulong UserId { get; set; }

    [BsonElement("guild_id")] public ulong GuildId { get; set; }

    [BsonElement("games")]
    [BsonDictionaryOptions(DictionaryRepresentation.Document)]
    public Dictionary<string, SingleGameStats> Games { get; set; } = new();
}