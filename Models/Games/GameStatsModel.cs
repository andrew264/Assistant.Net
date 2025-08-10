using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Options;

namespace Assistant.Net.Models.Games;

public abstract class GameStatsModel
{
    [BsonId] public GameStatsIdKey Id { get; set; }

    [BsonElement("games")]
    [BsonDictionaryOptions(DictionaryRepresentation.Document)]
    public Dictionary<string, SingleGameStats> Games { get; set; } = new();
}