using Assistant.Net.Configuration;
using Assistant.Net.Models.Reddit;
using Assistant.Net.Services.ExternalApis;
using Assistant.Net.Utilities;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Utility.Interaction;

public class RedditModule(
    RedditService redditService,
    Config config,
    ILogger<RedditModule> logger)
    : InteractionModuleBase<SocketInteractionContext>
{
    // Constants
    private const int MaxPosts = 5;
    private const string DefaultTimeFilter = "week";
    private const string GalleryDomain = "www.reddit.com/gallery";
    private static readonly HashSet<string> VideoDomains = ["redgifs.com", "v.redd.it", "imgur.com", "i.imgur.com"];
    private static readonly Random Random = Random.Shared;

    // --- Helper Methods ---

    private ProcessedRedditPost? ProcessSubmission(RedditPostData submission)
    {
        if (string.IsNullOrWhiteSpace(submission.Url)) return null;

        var contentUrl = submission.Url;
        if (contentUrl.EndsWith(".gifv") && contentUrl.Contains("imgur.com"))
        {
            contentUrl = contentUrl[..^1];
            logger.LogTrace("Processed .gifv URL: {Original} -> {Processed}", submission.Url, contentUrl);
        }

        var isGallery = contentUrl.Contains(GalleryDomain);
        var isVideo = VideoDomains.Any(domain => contentUrl.Contains(domain, StringComparison.OrdinalIgnoreCase))
                      || contentUrl.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                      || contentUrl.EndsWith(".mov", StringComparison.OrdinalIgnoreCase)
                      || contentUrl.EndsWith(".webm", StringComparison.OrdinalIgnoreCase);

        // Treat galleries as non-videos for initial embed display
        if (isGallery) isVideo = false;

        return new ProcessedRedditPost
        {
            Submission = submission,
            ContentUrl = contentUrl,
            IsGallery = isGallery,
            IsVideo = isVideo
        };
    }

    private async Task SendPostAsync(ProcessedRedditPost post)
    {
        var component = new ComponentBuilderV2();
        var container = new ContainerBuilder();

        // --- Header Section ---
        container.WithTextDisplay(new TextDisplayBuilder($"# {post.Submission.Title.Truncate(250)}"));
        var authorText = !string.IsNullOrWhiteSpace(post.Submission.Author)
            ? $"by u/{post.Submission.Author}"
            : "by [deleted]";
        container.WithTextDisplay(new TextDisplayBuilder($"{authorText} in r/{post.Submission.Subreddit}"));


        // --- Content Section ---
        container.WithSeparator();
        if (post.IsVideo)
        {
            container.WithTextDisplay(new TextDisplayBuilder("🎬 A video link will be sent in a separate message."));
        }
        else if (post.IsGallery)
        {
            container.WithTextDisplay(
                new TextDisplayBuilder("🖼️ This is a gallery post. Use the button below to view."));
        }
        else if (post.ContentUrl.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                 post.ContentUrl.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                 post.ContentUrl.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                 post.ContentUrl.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ||
                 post.ContentUrl.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
        {
            container.WithMediaGallery(new List<string> { post.ContentUrl });
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(post.Submission.SelfText))
                container.WithTextDisplay(
                    new TextDisplayBuilder(post.Submission.SelfText.Truncate(1024)));

            if (!string.IsNullOrWhiteSpace(post.ContentUrl) && !post.ContentUrl.Contains("www.reddit.com"))
                container.WithTextDisplay(new TextDisplayBuilder($"🔗 {post.ContentUrl}"));
        }

        // --- Stats & Footer Section ---
        container.WithSeparator();
        container.WithTextDisplay(new TextDisplayBuilder(
            $"👍 {post.Submission.Score:N0}  •  💬 {post.Submission.NumComments:N0}  •  {post.Submission.CreatedDateTime.GetRelativeTime()}"));

        // --- Action Row ---
        container.WithActionRow(row =>
        {
            row.WithButton("View on Reddit", style: ButtonStyle.Link, url: post.Submission.FullPermalink);
            if (post.IsGallery)
                row.WithButton("View Gallery", style: ButtonStyle.Link, url: post.ContentUrl);
        });


        component.WithContainer(container);

        await FollowupAsync(components: component.Build(), flags: MessageFlags.ComponentsV2).ConfigureAwait(false);

        if (post.IsVideo)
            // Send the video URL in a separate message for auto-embedding by Discord
            await FollowupAsync(post.ContentUrl).ConfigureAwait(false);
    }

    // --- Commands ---
    [SlashCommand("meme", "Get fresh memes from popular subreddits.")]
    [DefaultMemberPermissions(GuildPermission.SendMessages)]
    public async Task MemeCommand(
        [Summary("count", "Number of memes to fetch (1-5).")] [MinValue(1)] [MaxValue(MaxPosts)]
        int count = 1)
    {
        await DeferAsync().ConfigureAwait(false);

        var memeSubs = config.Reddit.MemeSubreddits;
        if (memeSubs.Count == 0)
        {
            await FollowupAsync("🚫 Meme subreddits are not configured.", ephemeral: true).ConfigureAwait(false);
            logger.LogWarning("Meme command failed: MemeSubreddits not configured in config.yaml.");
            return;
        }

        var subreddit = memeSubs[Random.Next(memeSubs.Count)];
        logger.LogInformation("Fetching {Count} meme(s) from r/{Subreddit}", count, subreddit);

        try
        {
            var posts = await GetRandomPostsAsync(subreddit, DefaultTimeFilter, false, count).ConfigureAwait(false);
            if (posts.Count == 0)
            {
                await FollowupAsync($"🚫 Couldn't find any suitable memes in r/{subreddit} right now.",
                    ephemeral: true).ConfigureAwait(false);
                return;
            }

            await FollowupAsync($"Found {posts.Count} meme(s) from r/{subreddit}. Sending now...", ephemeral: true)
                .ConfigureAwait(false);

            foreach (var post in posts)
            {
                await SendPostAsync(post!).ConfigureAwait(false);
                await Task.Delay(1100).ConfigureAwait(false);
            }
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Reddit configuration error during meme command.");
            await FollowupAsync($"❌ {ex.Message}", ephemeral: true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing meme command for r/{Subreddit}", subreddit);
            await FollowupAsync("❌ Failed to fetch memes. Please try again later.", ephemeral: true)
                .ConfigureAwait(false);
        }
    }

    [SlashCommand("nsfw", "Fetch NSFW content from Reddit.")]
    [RequireNsfw]
    [DefaultMemberPermissions(GuildPermission.SendMessages)]
    public async Task NsfwCommand(
        [Summary("count", "Number of posts to fetch (1-5).")] [MinValue(1)] [MaxValue(MaxPosts)]
        int count = 1)
    {
        await DeferAsync().ConfigureAwait(false);

        var nsfwSubs = config.Reddit.NsfwSubreddits;
        if (nsfwSubs.Count == 0)
        {
            await FollowupAsync("🚫 NSFW subreddits are not configured.", ephemeral: true).ConfigureAwait(false);
            logger.LogWarning("NSFW command failed: NsfwSubreddits not configured");
            return;
        }

        var subreddit = nsfwSubs[Random.Next(nsfwSubs.Count)];
        logger.LogInformation("Fetching {Count} NSFW post(s) from r/{Subreddit}", count, subreddit);

        try
        {
            var posts = await GetRandomPostsAsync(subreddit, DefaultTimeFilter, true, count).ConfigureAwait(false);
            if (posts.Count == 0)
            {
                await FollowupAsync($"🚫 Couldn't find any suitable NSFW content in r/{subreddit} right now.",
                    ephemeral: true).ConfigureAwait(false);
                return;
            }

            // Send initial confirmation
            await FollowupAsync($"Found {posts.Count} NSFW post(s) from r/{subreddit}. Sending now...",
                ephemeral: true).ConfigureAwait(false);

            foreach (var post in posts)
            {
                await SendPostAsync(post!).ConfigureAwait(false);
                await Task.Delay(1100).ConfigureAwait(false);
            }
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Reddit configuration error during NSFW command.");
            await FollowupAsync($"❌ {ex.Message}", ephemeral: true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing NSFW command for r/{Subreddit}", subreddit);
            await FollowupAsync("❌ Failed to fetch NSFW content. Please try again later.", ephemeral: true)
                .ConfigureAwait(false);
        }
    }

    private async Task<List<ProcessedRedditPost?>> GetRandomPostsAsync(string subreddit, string timeFilter,
        bool allowNsfw, int limit)
    {
        if (!config.Reddit.IsValid)
            throw new InvalidOperationException("Reddit client is not configured in config.yaml.");

        var postsData =
            await redditService.GetTopPostsAsync(subreddit, 100, timeFilter, allowNsfw)
                .ConfigureAwait(false); // Fetch more to sample from

        if (postsData == null) throw new Exception($"Failed to fetch posts from r/{subreddit}.");
        if (postsData.Count == 0) return [];

        var processedPosts = postsData
            .Select(ProcessSubmission)
            .Where(p => p != null)
            .ToList();

        if (processedPosts.Count == 0) return [];

        // Fisher-Yates shuffle
        var n = processedPosts.Count;
        while (n > 1)
        {
            n--;
            var k = Random.Next(n + 1);
            (processedPosts[k], processedPosts[n]) = (processedPosts[n], processedPosts[k]);
        }

        return processedPosts.Take(limit).ToList();
    }
}