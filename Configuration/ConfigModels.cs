namespace Assistant.Net.Configuration;

public record Config
{
    public ClientConfig Client { get; init; } = new();
    public MongoConfig Mongo { get; init; } = new();
    public RedditConfig Reddit { get; init; } = new();
    public LavalinkConfig Lavalink { get; init; } = new();
    public MusicConfig Music { get; init; } = new();
    public string? YoutubeApiKey { get; init; }
    public string? GeniusToken { get; init; }
    public string? TenorApiKey { get; init; }
    public Dictionary<string, LoggingGuildConfig>? LoggingGuilds { get; init; }
    public string ResourcePath { get; init; } = "Resources";
}

public record ClientConfig
{
    public string? Token { get; init; }
    public string? Prefix { get; init; }
    public ulong? OwnerId { get; init; }
    public string LogLevel { get; init; } = "Information";
    public string Status { get; init; } = "Online";
    public string ActivityType { get; init; } = "Playing";
    public string? ActivityText { get; init; }
    public ulong HomeGuildId { get; init; }
    public List<ulong>? TestGuilds { get; init; }
    public ulong DmRecipientsCategory { get; init; }
}

public record MongoConfig
{
    // Option 1: User/Pass/Url
    public string? Username { get; init; }
    public string? Password { get; init; }
    public string? Url { get; init; }

    // Option 2: Full Connection String
    public string? ConnectionString { get; init; }

    public string DatabaseName { get; init; } = "assistant_cs";

    // Helper to construct the connection string if needed
    public string GetConnectionString()
    {
        if (!string.IsNullOrWhiteSpace(ConnectionString)) return ConnectionString;
        if (!string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password) &&
            !string.IsNullOrWhiteSpace(Url))
        {
            // Ensure URL doesn't contain protocol, driver handles it
            var cleanUrl = Url.Replace("mongodb+srv://", "").Replace("mongodb://", "");
            return $"mongodb+srv://{Uri.EscapeDataString(Username)}:{Uri.EscapeDataString(Password)}@{cleanUrl}";
        }

        throw new InvalidOperationException(
            "MongoDB connection details are incomplete. Provide either ConnectionString or Username/Password/Url.");
    }
}

public record RedditConfig
{
    public string? ClientId { get; init; }
    public string? ClientSecret { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }

    public bool IsValid => !string.IsNullOrWhiteSpace(ClientId) &&
                           !string.IsNullOrWhiteSpace(ClientSecret) &&
                           !string.IsNullOrWhiteSpace(Username) &&
                           !string.IsNullOrWhiteSpace(Password);

    public List<string>? MemeSubreddits { get; init; }
    public List<string>? NsfwSubreddits { get; init; }
}

public record LavalinkConfig
{
    public string? Uri { get; init; }
    public string? Password { get; init; }

    public bool IsValid => !string.IsNullOrWhiteSpace(Uri) && !string.IsNullOrWhiteSpace(Password);
}

public record LoggingGuildConfig
{
    public ulong GuildId { get; init; }
    public ulong ChannelId { get; init; }
}

public record MusicConfig
{
    public int MaxHistorySize { get; init; } = 100;
    public float DefaultVolume { get; init; } = 0.30f; // 30%
    public float TitleSimilarityCutoff { get; init; } = 0.80f; // 80%
    public float UriSimilarityCutoff { get; init; } = 0.70f; // 70%
}