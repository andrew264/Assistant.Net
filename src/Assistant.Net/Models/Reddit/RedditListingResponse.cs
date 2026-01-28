using Newtonsoft.Json;

namespace Assistant.Net.Models.Reddit;

// Represents the top-level response from a Reddit listing endpoint
public class RedditListingResponse
{
    [JsonProperty("kind")] public string Kind { get; set; } = string.Empty;

    [JsonProperty("data")] public RedditListingData Data { get; set; } = new();
}