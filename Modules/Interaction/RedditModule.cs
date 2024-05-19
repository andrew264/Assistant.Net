using Assistant.Net.Modules.Interaction.Preconditions;
using Assistant.Net.Services;
using Discord;
using Discord.Interactions;
using Reddit.Controllers;

namespace Assistant.Net.Modules.Interaction;
public class RedditModule : InteractionModuleBase<SocketInteractionContext>
{
    public required RedditService Reddit { get; set; }

    private static readonly List<string> _memeSubreddits =
    [
        "memes",
        "dankmemes",
        "wholesomememes",
        "me_irl",
        "meirl",
        "2meirl4meirl",
        "comedyheaven",
        "funny",
    ];

    private static readonly List<string> _nsfwSubreddits =
    [
        "nsfw",
        "gonewild",
        "NSFW_GIF",
        "legalteens",
        "realgirls",
        "rule34",
        "ass",
        "girlsinyogapants",
        "boobies",
        "celebnsfw",
        "lesbians",
        "NSFW_HTML5"
    ];
    private static readonly List<string> _gifSites = ["redgifs.com", "v.redd.it", "imgur.com", "gfycat.com"];

    private Reddit.Things.Post FetchTopRedditPost(string subreddit, string uploaded = "week", bool over18 = false)
    {
        var sub = Reddit.Client.Subreddit(subreddit);
        List<Post>? topPosts = sub.Posts.GetTop(uploaded, limit: 25);
        if (over18)
            topPosts = [.. topPosts.Where(x => x.NSFW)];
        return topPosts.ElementAt(new Random().Next(0, topPosts.Count)).Listing;
    }

    private static (Embed, MessageComponent?, string?) CreateRedditEmbed(Reddit.Things.Post post)
    {
        var embed = new EmbedBuilder
        {
            Color = new Color(0xFF5700),
            Title = post.Title,
            Author = new EmbedAuthorBuilder
            {
                Name = $"Uploaded by u/{post.Author} on r/{post.Subreddit}",
                Url = "https://reddit.com" + post.Permalink,
            },
            Footer = new EmbedFooterBuilder
            {
                Text = $"👍 {post.Ups} | 💬 {post.NumComments} | 🕒 {post.CreatedUTC}",
            }
        };
        if (_gifSites.Any(x => post.URL.Contains(x)))
        {
            return (embed.Build(), null, post.URL);
        }
        else if (post.URL.Contains("www.reddit.com/gallery"))
        {
            var component = new ComponentBuilder().WithButton("View Gallery", style: ButtonStyle.Link, emote: new Emoji("🖼️"), url: post.URL);
            return (embed.Build(), component.Build(), null);
        }
        else
        {
            embed.ImageUrl = post.URL;
            return (embed.Build(), null, null);
        }
    }

    [SlashCommand("meme", "Get a random meme from Reddit")]
    public async Task GetMemeAsync([Summary(description: "Sort by")] RedditSort sort = RedditSort.week)
    {
        await DeferAsync();
        var post = FetchTopRedditPost(_memeSubreddits.ElementAt(new Random().Next(0, _memeSubreddits.Count)),
            uploaded: sort.ToString());

        var (embed, component, url) = CreateRedditEmbed(post);
        await ModifyOriginalResponseAsync(x =>
        {
            x.Embed = embed;
            x.Components = component;
        });
        if (url != null)
            await Context.Channel.SendMessageAsync(post.URL);
    }

    [RequireNsfwOrDm]
    [SlashCommand("nsfw", "Get a random NSFW post from Reddit")]
    public async Task GetNsfwPostAsync([Summary(description: "Sort by")] RedditSort sort = RedditSort.week)
    {
        await DeferAsync();
        var post = FetchTopRedditPost(_nsfwSubreddits.ElementAt(new Random().Next(0, _nsfwSubreddits.Count)),
            uploaded: sort.ToString(),
            over18: true);

        var (embed, component, url) = CreateRedditEmbed(post);
        await ModifyOriginalResponseAsync(x =>
        {
            x.Embed = embed;
            x.Components = component;
        });
        if (url != null)
            await Context.Channel.SendMessageAsync(post.URL);
    }

    public enum RedditSort
    {
        [ChoiceDisplay("Hour")] hour,
        [ChoiceDisplay("Day")] day,
        [ChoiceDisplay("Week")] week,
        [ChoiceDisplay("Month")] month,
        [ChoiceDisplay("Year")] year,
        [ChoiceDisplay("All Time")] all
    }
}
