using System.Text.Json.Serialization;

namespace Assistant.Net.Models.Reddit;

public class RedditListingData
{
    [JsonPropertyName("children")] public List<RedditPostContainer> Children { get; set; } = [];

    [JsonPropertyName("after")] public string? After { get; set; }

    [JsonPropertyName("before")] public string? Before { get; set; }

    [JsonPropertyName("dist")] public int? Dist { get; set; }
}