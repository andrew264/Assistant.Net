using System.Text.Json.Serialization;

namespace Assistant.Net.Models.Reddit;

public class RedditPostData
{
    [JsonPropertyName("subreddit")] public string Subreddit { get; set; } = string.Empty;

    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;

    [JsonPropertyName("author")] public string Author { get; set; } = string.Empty;

    [JsonPropertyName("name")] public string Fullname { get; set; } = string.Empty;

    [JsonPropertyName("ups")] public int Score { get; set; }

    [JsonPropertyName("thumbnail")] public string Thumbnail { get; set; } = string.Empty;

    [JsonPropertyName("created_utc")] public double CreatedUtc { get; set; }

    [JsonPropertyName("permalink")] public string Permalink { get; set; } = string.Empty;

    [JsonPropertyName("url")] public string Url { get; set; } = string.Empty;

    [JsonPropertyName("num_comments")] public int NumComments { get; set; }

    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;

    [JsonPropertyName("is_video")] public bool IsVideo { get; set; }

    [JsonPropertyName("over_18")] public bool IsNsfw { get; set; }

    [JsonPropertyName("selftext")] public string SelfText { get; set; } = string.Empty;

    [JsonIgnore] public DateTimeOffset CreatedDateTime => DateTimeOffset.FromUnixTimeSeconds((long)CreatedUtc);

    [JsonIgnore] public string FullPermalink => $"https://www.reddit.com{Permalink}";
}