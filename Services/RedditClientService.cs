using Assistant.Net.Utils;
using Reddit;

namespace Assistant.Net.Services;

public class RedditService(Config config)
{

    public RedditClient Client = new(config.reddit.client_id,
        config.reddit.refresh_token,
        config.reddit.client_secret,
        userAgent: config.reddit.user_agent);

}