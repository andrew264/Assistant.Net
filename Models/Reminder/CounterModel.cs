using MongoDB.Bson.Serialization.Attributes;

namespace Assistant.Net.Models.Reminder;

public class CounterModel
{
    [BsonId] public string Id { get; set; } = null!;

    [BsonElement("seq")] public int SequenceValue { get; set; }
}