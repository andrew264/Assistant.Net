using Newtonsoft.Json;

namespace Assistant.Net.Models.Reddit;

public class RedditPostData
{
    [JsonProperty("subreddit")] public string Subreddit { get; set; } = string.Empty;

    [JsonProperty("title")] public string Title { get; set; } = string.Empty;

    [JsonProperty("author")] public string Author { get; set; } = string.Empty;

    [JsonProperty("name")] public string Fullname { get; set; } = string.Empty;

    [JsonProperty("ups")] public int Score { get; set; }

    [JsonProperty("thumbnail")] public string Thumbnail { get; set; } = string.Empty;

    [JsonProperty("created_utc")] public double CreatedUtc { get; set; }

    [JsonProperty("permalink")] public string Permalink { get; set; } = string.Empty;

    [JsonProperty("url")] public string Url { get; set; } = string.Empty;

    [JsonProperty("num_comments")] public int NumComments { get; set; }

    [JsonProperty("id")] public string Id { get; set; } = string.Empty;

    [JsonProperty("is_video")] public bool IsVideo { get; set; }

    [JsonProperty("over_18")] public bool IsNsfw { get; set; }

    [JsonProperty("selftext")] public string SelfText { get; set; } = string.Empty;

    [JsonIgnore] public DateTimeOffset CreatedDateTime => DateTimeOffset.FromUnixTimeSeconds((long)CreatedUtc);

    [JsonIgnore] public string FullPermalink => $"https://www.reddit.com{Permalink}";
}