using System.Text.Json.Serialization;

namespace Assistant.Net.Models.Reddit;

public class RedditListingResponse
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("data")] public RedditListingData Data { get; set; } = new();
}