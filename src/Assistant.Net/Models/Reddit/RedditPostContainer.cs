using System.Text.Json.Serialization;

namespace Assistant.Net.Models.Reddit;

public class RedditPostContainer
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("data")] public RedditPostData Data { get; set; } = new();
}