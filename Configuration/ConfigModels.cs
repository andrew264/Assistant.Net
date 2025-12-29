using YamlDotNet.Serialization;

namespace Assistant.Net.Configuration;

public class Config
{
    public ClientConfig Client { get; set; } = new();
    public LavalinkConfig Lavalink { get; set; } = new();
    public RedditConfig Reddit { get; set; } = new();
    public MusicConfig Music { get; set; } = new();
    public DatabaseConfig Database { get; set; } = new();
    public Dictionary<string, LoggingGuildConfig>? LoggingGuilds { get; set; }

    [YamlIgnore] public string ResourcePath { get; set; } = "Resources";

    public string GeniusToken { get; set; } = string.Empty;
}

public class ClientConfig
{
    public string? Token { get; set; }
    public ulong? OwnerId { get; set; }
    public List<ulong> TestGuilds { get; set; } = [];
    public string Status { get; set; } = "Online";
    public string ActivityType { get; set; } = "Playing";
    public string? ActivityText { get; set; }
    public string? Prefix { get; set; }
    public string LogLevel { get; set; } = "Information";
    public ulong HomeGuildId { get; set; }
    public ulong DmRecipientsCategory { get; set; }
}

public class LavalinkConfig
{
    public List<LavalinkNode> Nodes { get; set; } = [];
    public bool IsValid => Nodes.Count > 0;
}

public class LavalinkNode
{
    public string Name { get; set; } = "default";
    public string Uri { get; set; } = "http://localhost:2333";
    public string Password { get; set; } = "youshallnotpass";
}

public class RedditConfig
{
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

public class MusicConfig
{
    public float DefaultVolume { get; set; } = 1.0f;
    public int MaxPlayerVolumePercent { get; set; } = 200;
    public double TitleSimilarityCutoff { get; set; } = 0.4;
}

public class DatabaseConfig
{
    public string ConnectionString { get; set; } = string.Empty;
}

public class LoggingGuildConfig
{
    public ulong GuildId { get; set; }
    public ulong ChannelId { get; set; }
    public bool LogPresenceUpdates { get; set; } = false;
}