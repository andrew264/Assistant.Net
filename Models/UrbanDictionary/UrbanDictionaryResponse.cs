using Newtonsoft.Json;

namespace Assistant.Net.Models.UrbanDictionary;

public class UrbanDictionaryResponse
{
    [JsonProperty("list")] public List<UrbanDictionaryEntry> List { get; set; } = [];
}