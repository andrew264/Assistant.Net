using System.Text.Json.Serialization;

namespace Assistant.Net.Models.UrbanDictionary;

public class UrbanDictionaryResponse
{
    [JsonPropertyName("list")] public List<UrbanDictionaryEntry> List { get; set; } = [];
}