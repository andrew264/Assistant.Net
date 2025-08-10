using MongoDB.Bson.Serialization.Attributes;

namespace Assistant.Net.Models.Reminder;

public abstract class CounterModel
{
    [BsonId] public string Id { get; set; } = null!;

    [BsonElement("seq")] public int SequenceValue { get; set; }
}