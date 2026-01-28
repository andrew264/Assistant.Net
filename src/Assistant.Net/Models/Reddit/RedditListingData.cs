using Newtonsoft.Json;

namespace Assistant.Net.Models.Reddit;

// Represents the "data" object within the main listing response
public class RedditListingData
{
    [JsonProperty("children")] public List<RedditPostContainer> Children { get; set; } = [];

    [JsonProperty("after")] public string? After { get; set; }

    [JsonProperty("before")] public string? Before { get; set; }

    [JsonProperty("dist")] public int? Dist { get; set; }
}