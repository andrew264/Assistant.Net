using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Assistant.Net.Configuration;
using Assistant.Net.Models.Reddit;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Assistant.Net.Services;

public class RedditService(
    IHttpClientFactory httpClientFactory,
    IMemoryCache memoryCache,
    ILogger<RedditService> logger,
    Config config)
{
    private const string OauthUrl = "https://oauth.reddit.com";
    private const string TokenUrl = "https://www.reddit.com/api/v1/access_token";
    private const string CachePrefix = "reddit:top:";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(4);
    private static readonly TimeSpan TokenPadding = TimeSpan.FromMinutes(5);

    private static readonly HashSet<string> ValidTimeframes =
        ["hour", "day", "week", "month", "year", "all"];

    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private string? _accessToken;
    private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;

    private async Task<string?> GetAccessTokenAsync()
    {
        if (!config.Reddit.IsValid)
        {
            logger.LogWarning("Cannot get Reddit access token: Reddit configuration is invalid.");
            return null;
        }

        if (!string.IsNullOrEmpty(_accessToken) && _tokenExpiry > DateTimeOffset.UtcNow.Add(TokenPadding))
        {
            logger.LogTrace("Using cached Reddit access token.");
            return _accessToken;
        }

        await _tokenLock.WaitAsync();
        try
        {
            if (!string.IsNullOrEmpty(_accessToken) && _tokenExpiry > DateTimeOffset.UtcNow.Add(TokenPadding))
            {
                logger.LogTrace("Using cached Reddit access token (checked after lock).");
                return _accessToken;
            }

            logger.LogInformation("Requesting new Reddit API access token...");

            var httpClient = httpClientFactory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl);

            var authValue =
                Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"{config.Reddit.ClientId}:{config.Reddit.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authValue);

            var botUsername = config.Reddit.Username ?? "UnknownBot";
            var userAgent = $"Assistant.Net/1.0 by /u/{botUsername}";
            request.Headers.TryAddWithoutValidation("User-Agent", userAgent);

            var formData = new Dictionary<string, string>
            {
                { "grant_type", "password" },
                { "username", config.Reddit.Username! },
                { "password", config.Reddit.Password! }
            };
            request.Content = new FormUrlEncodedContent(formData);

            HttpResponseMessage response;
            try
            {
                response = await httpClient.SendAsync(request);
            }
            catch (HttpRequestException ex)
            {
                logger.LogError(ex, "HTTP error occurred while requesting Reddit access token.");
                _accessToken = null;
                _tokenExpiry = DateTimeOffset.MinValue;
                return null;
            }

            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError(
                    "Failed to get Reddit access token. Status: {StatusCode}, Reason: {ReasonPhrase}, Body: {Body}",
                    response.StatusCode, response.ReasonPhrase, responseBody);
                _accessToken = null;
                _tokenExpiry = DateTimeOffset.MinValue;
                return null;
            }

            try
            {
                var jsonResponse = JObject.Parse(responseBody);
                var token = jsonResponse["access_token"]?.Value<string>();
                var expiresIn = jsonResponse["expires_in"]?.Value<int>();

                if (string.IsNullOrEmpty(token) || !expiresIn.HasValue)
                {
                    logger.LogError("Reddit access token response did not contain expected fields. Body: {Body}",
                        responseBody);
                    _accessToken = null;
                    _tokenExpiry = DateTimeOffset.MinValue;
                    return null;
                }

                _accessToken = token;
                _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(expiresIn.Value);

                logger.LogInformation("Successfully obtained new Reddit API access token. Expires at: {Expiry}",
                    _tokenExpiry);
                return _accessToken;
            }
            catch (JsonException ex)
            {
                logger.LogError(ex, "Failed to parse Reddit access token response. Body: {Body}", responseBody);
                _accessToken = null;
                _tokenExpiry = DateTimeOffset.MinValue;
                return null;
            }
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    public async Task<List<RedditPostData>?> GetTopPostsAsync(string subreddit, int limit = 25,
        string timeframe = "day", bool? allowNsfw = null)
    {
        if (string.IsNullOrWhiteSpace(subreddit) || subreddit.Contains('/') || subreddit.Contains(' '))
        {
            logger.LogWarning("GetTopPostsAsync called with empty or invalid subreddit name: '{Subreddit}'", subreddit);
            return [];
        }

        subreddit = subreddit.Trim();

        if (limit is < 1 or > 100)
        {
            logger.LogWarning("Invalid limit '{Limit}' requested for subreddit '{Subreddit}'. Clamping to 1-100.",
                limit, subreddit);
            limit = Math.Clamp(limit, 1, 100);
        }

        timeframe = timeframe.ToLowerInvariant();
        if (!ValidTimeframes.Contains(timeframe))
        {
            logger.LogWarning(
                "Invalid timeframe '{Timeframe}' requested for subreddit '{Subreddit}'. Defaulting to 'day'.",
                timeframe, subreddit);
            timeframe = "day";
        }

        var cacheKey = $"{CachePrefix}{subreddit}:{limit}:{timeframe}:nsfw-{allowNsfw?.ToString() ?? "any"}";
        if (memoryCache.TryGetValue(cacheKey, out List<RedditPostData>? cachedPosts) && cachedPosts != null)
        {
            logger.LogDebug("Cache hit for Reddit top posts: {Key}", cacheKey);
            return cachedPosts;
        }

        logger.LogDebug("Cache miss for Reddit top posts: {Key}", cacheKey);

        var token = await GetAccessTokenAsync();
        if (token == null)
        {
            logger.LogError("Failed to get Reddit access token. Cannot fetch posts for r/{Subreddit}.", subreddit);
            return null;
        }

        var botUsername = config.Reddit.Username ?? "UnknownBot";
        var userAgent = $"Assistant.Net/1.0 by /u/{botUsername}";

        var apiUrl = $"{OauthUrl}/r/{subreddit}/top.json?limit={limit}&t={timeframe}";
        HttpResponseMessage response;

        try
        {
            var httpClient = httpClientFactory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);

            request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            response = await httpClient.SendAsync(request);

            switch (response.StatusCode)
            {
                case HttpStatusCode.NotFound:
                    logger.LogInformation("Subreddit 'r/{Subreddit}' not found (404) via OAuth.", subreddit);
                    var emptyList404 = new List<RedditPostData>();
                    memoryCache.Set(cacheKey, emptyList404, CacheDuration);
                    return emptyList404;
                case HttpStatusCode.Forbidden:
                    logger.LogWarning(
                        "Access forbidden (403) via OAuth for subreddit 'r/{Subreddit}'. It might be private or the bot's account lacks access.",
                        subreddit);
                    var emptyList403 = new List<RedditPostData>();
                    memoryCache.Set(cacheKey, emptyList403, CacheDuration);
                    return emptyList403;
                case HttpStatusCode.Unauthorized:
                    logger.LogWarning(
                        "Reddit API request failed with Unauthorized (401). Token might be invalid. Invalidating local token.");
                    _accessToken = null;
                    _tokenExpiry = DateTimeOffset.MinValue;
                    return null;
                case (HttpStatusCode)302:
                    logger.LogInformation(
                        "Received redirect (302) via OAuth for subreddit 'r/{Subreddit}', likely doesn't exist or is private.",
                        subreddit);
                    var emptyList302 = new List<RedditPostData>();
                    memoryCache.Set(cacheKey, emptyList302, CacheDuration);
                    return emptyList302;
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                logger.LogError(
                    "Reddit API request failed (OAuth). Status: {StatusCode}, Reason: {ReasonPhrase}, URL: {Url}, Body: {Body}",
                    response.StatusCode, response.ReasonPhrase, apiUrl, errorBody);
                return null;
            }

            var jsonString = await response.Content.ReadAsStringAsync();
            var listingResponse = JsonConvert.DeserializeObject<RedditListingResponse>(jsonString);

            if (listingResponse?.Data?.Children == null)
            {
                logger.LogWarning(
                    "Failed to parse Reddit response or received unexpected structure (OAuth) for URL: {Url}", apiUrl);
                return null;
            }

            var posts = listingResponse.Data.Children
                .Where(c => c.Kind == "t3")
                .Select(c => c.Data)
                .ToList();

            if (allowNsfw.HasValue)
            {
                posts = posts.Where(p => p.IsNsfw == allowNsfw.Value).ToList();
                logger.LogDebug(
                    "Filtered posts for r/{Subreddit}. NSFW Allowed: {AllowNsfw}. Count after filter: {Count}",
                    subreddit, allowNsfw, posts.Count);
            }

            logger.LogInformation(
                "Successfully fetched {Count} top posts from r/{Subreddit} (Limit: {Limit}, Timeframe: {Timeframe}) using OAuth.",
                posts.Count, subreddit, limit, timeframe);

            memoryCache.Set(cacheKey, posts, CacheDuration);
            return posts;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HTTP error occurred while fetching posts (OAuth) from r/{Subreddit}", subreddit);
            return null;
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "JSON error occurred while parsing Reddit response (OAuth) for r/{Subreddit}",
                subreddit);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error occurred while fetching posts (OAuth) from r/{Subreddit}", subreddit);
            return null;
        }
    }
}