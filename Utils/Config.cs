using Discord;
using Tomlyn;

namespace Assistant.Net.Utils
{
    public class Config
    {
        public Client client { get; set; }

        public static Config LoadFromFile(string filePath)
        {
            return Toml.ToModel<Config>(File.ReadAllText(filePath));
        }
    }
    public class Client
    {
        public string token { get; set; }
        public string prefix { get; set; }
        public string status { get; set; }
        public string activity_type { get; set; }
        public string activity_text { get; set; }

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
}
