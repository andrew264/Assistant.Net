namespace Assistant.Net.Models.Reddit;

/// <summary>
///     Represents a processed Reddit submission ready for display.
/// </summary>
public record ProcessedRedditPost
{
    public RedditPostData Submission { get; init; } = null!;
    public string ContentUrl { get; init; } = string.Empty;
    public bool IsGallery { get; init; }
    public bool IsVideo { get; init; }
}