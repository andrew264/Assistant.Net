using Assistant.Net.Configuration;
using Assistant.Net.Models.Reddit;
using Assistant.Net.Services;
using Assistant.Net.Utilities;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Misc;

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
    private static readonly Color RedditOrange = new(0xFF5700);
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
        var embed = new EmbedBuilder()
            .WithTitle(
                post.Submission.Title.Truncate(256))
            .WithColor(RedditOrange)
            .WithUrl(post.Submission.FullPermalink)
            .WithTimestamp(post.Submission.CreatedDateTime);

        embed.WithAuthor(
            !string.IsNullOrWhiteSpace(post.Submission.Author) ? $"u/{post.Submission.Author}" : "Deleted User",
            url: !string.IsNullOrWhiteSpace(post.Submission.Author)
                ? $"https://reddit.com/u/{post.Submission.Author}"
                : null
        );

        embed.WithFooter($"r/{post.Submission.Subreddit} | 👍 {post.Submission.Score}");

        var view = BuildRedditView(post);

        if (post.IsVideo)
        {
            await FollowupAsync(embed: embed.Build(), components: view.Build()).ConfigureAwait(false);
            await Task.Delay(250).ConfigureAwait(false);
            await FollowupAsync(post.ContentUrl).ConfigureAwait(false);
        }
        else if (post.IsGallery)
        {
            await FollowupAsync(embed: embed.Build(), components: view.Build()).ConfigureAwait(false);
        }
        else
        {
            if (post.ContentUrl.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                post.ContentUrl.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                post.ContentUrl.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                post.ContentUrl.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ||
                post.ContentUrl.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
                embed.WithImageUrl(post.ContentUrl);
            else
                embed.Description = $"{post.ContentUrl}\n\n{post.Submission.SelfText}";
            await FollowupAsync(embed: embed.Build(), components: view.Build()).ConfigureAwait(false);
        }
    }

    private static ComponentBuilder BuildRedditView(ProcessedRedditPost post)
    {
        var builder = new ComponentBuilder();
        builder.WithButton("View Post", style: ButtonStyle.Link, url: post.Submission.FullPermalink);
        if (post.IsGallery) builder.WithButton("View Gallery", style: ButtonStyle.Link, url: post.Submission.Url);
        return builder;
    }

    private async Task<List<ProcessedRedditPost?>> GetRandomPostsAsync(string subreddit, string timeFilter,
        bool allowNsfw, int limit)
    {
        if (!config.Reddit.IsValid)
            throw new InvalidOperationException("Reddit client is not configured in config.yaml.");

        var postsData =
            await redditService.GetTopPostsAsync(subreddit, 100, timeFilter, allowNsfw).ConfigureAwait(false); // Fetch more to sample from

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


    // --- Commands ---
    [SlashCommand("meme", "Get fresh memes from popular subreddits.")]
    [DefaultMemberPermissions(GuildPermission.SendMessages)]
    public async Task MemeCommand(
        [Summary("count", "Number of memes to fetch (1-5).")] [MinValue(1)] [MaxValue(MaxPosts)]
        int count = 1)
    {
        await DeferAsync().ConfigureAwait(false);

        var memeSubs = config.Reddit.MemeSubreddits;
        if (memeSubs == null || memeSubs.Count == 0)
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

            await FollowupAsync($"Found {posts.Count} meme(s) from r/{subreddit}. Sending now...", ephemeral: true).ConfigureAwait(false);

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
            await FollowupAsync("❌ Failed to fetch memes. Please try again later.", ephemeral: true).ConfigureAwait(false);
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
        if (nsfwSubs == null || nsfwSubs.Count == 0)
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
            await FollowupAsync("❌ Failed to fetch NSFW content. Please try again later.", ephemeral: true).ConfigureAwait(false);
        }
    }
}