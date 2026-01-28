namespace Assistant.Net.Options;

public sealed class RedditOptions
{
    public const string SectionName = "Reddit";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public List<string> MemeSubreddits { get; set; } = [];
    public List<string> NsfwSubreddits { get; set; } = [];

    public bool IsValid => !string.IsNullOrWhiteSpace(ClientId) &&
                           !string.IsNullOrWhiteSpace(ClientSecret) &&
                           !string.IsNullOrWhiteSpace(Username) &&
                           !string.IsNullOrWhiteSpace(Password);
}