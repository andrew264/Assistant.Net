using Reddit;

namespace Assistant.Net.Services;

public class RedditService(BotConfig config)
{

    public RedditClient Client = new(config.reddit.client_id,
        config.reddit.refresh_token,
        config.reddit.client_secret,
        userAgent: config.reddit.user_agent);

}