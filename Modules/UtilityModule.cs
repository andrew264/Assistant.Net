using Assistant.Net.Services;
using Discord;
using Discord.Interactions;
using Discord.Webhook;
using Reddit.Controllers;

namespace Assistant.Net.Modules;

public class UtilityModule : InteractionModuleBase<SocketInteractionContext>
{
    public required InteractionService Commands { get; set; }

    public required UrbanDictionaryService UrbanDictionary { get; set; }

    public required MicrosoftTranslatorService MicrosoftTranslator { get; set; }

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


    [SlashCommand("ping", "Pings the bot and returns its latency.")]
    public async Task GreetUserAsync()
        => await RespondAsync(text: $"Pong! {Context.Client.Latency}ms", ephemeral: true);

    [SlashCommand("define", "Find wth does words mean from UrbanDictionary")]
    public async Task DefineWordAsync([Summary(description: "Enter a word")] string word = "")
    {
        await DeferAsync();
        var definition = await UrbanDictionary.GetDefinitionAsync(word);
        await FollowupAsync(text: definition.Substring(0, Math.Min(2000, definition.Length)));
        for (int i = 2000; i < definition.Length; i += 2000)
        {
            await Context.Channel.SendMessageAsync(text: definition.Substring(i, Math.Min(2000, definition.Length - i)));
        }
    }

    private static async Task<DiscordWebhookClient> GetWebhookClient(ITextChannel channel)
    {
        var webhooks = await channel.GetWebhooksAsync();
        var webhook = webhooks.FirstOrDefault(x => x.Name == "Assistant");

        if (webhook != null)
            return new DiscordWebhookClient(webhook);

        var newWebhook = await channel.CreateWebhookAsync("Assistant");
        return new DiscordWebhookClient(newWebhook);
    }

    [SlashCommand("translate", "Translate text to another language")]
    public async Task TranslateTextAsync(
        [Summary(description: "Enter the text to translate")] string text,
        [Summary(description: "Enter the language to translate to")] Languages to,
        [Summary(description: "Enter the language to translate from")] Languages? from = null)
    {

        if (Context.Channel is ITextChannel channel)
        {
            await RespondAsync("Translating...", ephemeral: true);
            var translation = await MicrosoftTranslator.TranslateAsync(text, to.ToString(), from?.ToString());
            var webhook = await GetWebhookClient(channel);
            await webhook.SendMessageAsync(
                text: translation,
                username: Context.User.GlobalName ?? Context.User.Username,
                avatarUrl: Context.User.GetAvatarUrl(size: 128)
            );
        }
        else
        {
            await DeferAsync();
            var translation = await MicrosoftTranslator.TranslateAsync(text, to.ToString(), from?.ToString());
            await ModifyOriginalResponseAsync(x => x.Content = translation);
        }
    }

    [MessageCommand("To English")]
    public async Task TranslateToEnglishAsync(IMessage message)
    {
        await DeferAsync();
        var translation = await MicrosoftTranslator.TranslateAsync(message.Content, Languages.en.ToString());
        await ModifyOriginalResponseAsync(x => x.Content = translation);
    }

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
}

public enum Languages
{
    [ChoiceDisplay("English")] en,
    [ChoiceDisplay("Japanese")] ja,
    [ChoiceDisplay("Tamil")] ta,
    [ChoiceDisplay("Spanish")] es,
    [ChoiceDisplay("French")] fr,
    [ChoiceDisplay("Hindi")] hi,
    [ChoiceDisplay("Russian")] ru,
    [ChoiceDisplay("German")] de,
    [ChoiceDisplay("Malayalam")] ml,
    [ChoiceDisplay("Bengali")] bn,
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

public class RequireNsfwOrDmAttribute : PreconditionAttribute
{
    public override Task<PreconditionResult> CheckRequirementsAsync(IInteractionContext context, ICommandInfo command, IServiceProvider services)
    {
        if ((context.Channel is ITextChannel textChannel && textChannel.IsNsfw) || (context.Channel is IDMChannel))
        {
            return Task.FromResult(PreconditionResult.FromSuccess());
        }

        return Task.FromResult(PreconditionResult.FromError(ErrorMessage ?? "This command works only in an NSFW channel or in DMs."));
    }
}