namespace Assistant.Net.Models.Reddit;

public record ProcessedRedditPost
{
    public RedditPostData Submission { get; init; } = null!;
    public string ContentUrl { get; init; } = string.Empty;
    public bool IsGallery { get; init; }
    public bool IsVideo { get; init; }
}