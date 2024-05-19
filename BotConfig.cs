using Discord;
using Tomlyn;

namespace Assistant.Net;

public class BotConfig
{
    public Client client { get; set; }
    public MicrosoftTranslatorConfig translator { get; set; }
    public RedditConfig reddit { get; set; }

    public static BotConfig LoadFromFile(string filePath)
    {
        return Toml.ToModel<BotConfig>(File.ReadAllText(filePath));
    }
}
public class Client
{
    public string token { get; set; }
    public string prefix { get; set; }
    public string status { get; set; }
    public string activity_type { get; set; }
    public string activity_text { get; set; }
    public ulong home_guild_id { get; set; }
    public ulong dm_category_id { get; set; }
    public ulong logging_channel_id { get; set; }

    public UserStatus getStatus()
    {
        return status switch
        {
            "online" => UserStatus.Online,
            "offline" => UserStatus.Offline,
            "idle" => UserStatus.Idle,
            "dnd" => UserStatus.DoNotDisturb,
            _ => UserStatus.Online
        };
    }

    public ActivityType getActivityType()
    {
        return activity_type switch
        {
            "playing" => ActivityType.Playing,
            "streaming" => ActivityType.Streaming,
            "listening" => ActivityType.Listening,
            "watching" => ActivityType.Watching,
            _ => ActivityType.Playing
        };
    }
}

public class MicrosoftTranslatorConfig
{
    public string key { get; set; }
    public string region { get; set; }
}

public class RedditConfig
{
    public string client_id { get; set; }
    public string client_secret { get; set; }
    public string refresh_token { get; set; }
    public string user_agent { get; set; }
}
