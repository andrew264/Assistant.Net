using Newtonsoft.Json;

namespace Assistant.Net.Models.Reddit;

// Represents one item in the "children" array
public class RedditPostContainer
{
    [JsonProperty("kind")] public string Kind { get; set; } = string.Empty;

    [JsonProperty("data")] public RedditPostData Data { get; set; } = new();
}