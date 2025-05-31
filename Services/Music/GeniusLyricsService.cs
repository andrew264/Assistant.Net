using System.Net.Http.Headers;
using System.Web;
using Assistant.Net.Configuration;
using Assistant.Net.Models.Lyrics;
using HtmlAgilityPack;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Assistant.Net.Services.Music;

public class GeniusLyricsService(
    IHttpClientFactory httpClientFactory,
    IMemoryCache memoryCache,
    Config config,
    ILogger<GeniusLyricsService> logger)
{
    private const string GeniusApiBaseUrl = "https://api.genius.com";
    private const string GeniusApiSearchPath = "/search?q=";
    private const string SearchCacheKeyPrefix = "genius:search:";
    private const string SongCacheKeyPrefix = "genius:song:";
    private const string LyricsCacheKeyPrefix = "genius:lyrics:";
    private const string HttpClientName = "GeniusClient";
    private const string GeniusBaseUrl = "https://genius.com";
    private const string UserAgent = "Assistant.Net/1.0";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6);

    public async Task<List<GeniusSong>?> SearchSongsAsync(string title, string? artist = null)
    {
        if (string.IsNullOrWhiteSpace(config.GeniusToken))
        {
            logger.LogError("Genius API token is not configured. Cannot search songs.");
            return null;
        }

        var searchQuery = artist == null ? title : $"{title} {artist}";
        var normalizedQuery = searchQuery.ToLowerInvariant().Trim();
        var cacheKey = $"{SearchCacheKeyPrefix}{normalizedQuery}";

        if (memoryCache.TryGetValue(cacheKey, out List<GeniusSong>? cachedSongs) && cachedSongs != null)
        {
            logger.LogDebug("Genius search cache hit for query: '{Query}'", normalizedQuery);
            return cachedSongs;
        }

        logger.LogDebug("Genius search cache miss for query: '{Query}'. Fetching from API.", normalizedQuery);

        var encodedQuery = HttpUtility.UrlEncode(searchQuery);
        var requestUrl = $"{GeniusApiBaseUrl}{GeniusApiSearchPath}{encodedQuery}";

        var httpClient = httpClientFactory.CreateClient(HttpClientName);
        var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.GeniusToken);
        request.Headers.UserAgent.ParseAdd(UserAgent);

        try
        {
            var response = await httpClient.SendAsync(request).ConfigureAwait(false);
            var responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError(
                    "Genius API request failed with status {StatusCode} for query '{Query}'. Response: {Response}",
                    response.StatusCode, searchQuery,
                    responseContent.Length > 500 ? responseContent[..500] : responseContent);
                return null;
            }

            var geniusResponse = JsonConvert.DeserializeObject<GeniusSearchResponse>(responseContent);

            if (geniusResponse?.Meta.Status != 200 || geniusResponse.Response?.Hits == null)
            {
                logger.LogWarning(
                    "Genius API returned non-200 status or invalid response structure for query '{Query}'. Meta Status: {MetaStatus}",
                    searchQuery, geniusResponse?.Meta.Status);
                return [];
            }

            var songs = geniusResponse.Response.Hits
                .Where(hit => hit.Type.Equals("song", StringComparison.OrdinalIgnoreCase))
                .Select(hit => hit.Result)
                .ToList();

            if (songs.Count == 0) logger.LogInformation("No songs found on Genius for query: '{Query}'", searchQuery);

            var cacheEntryOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(CacheDuration)
                .SetSize(1);

            memoryCache.Set(cacheKey, songs, cacheEntryOptions);
            logger.LogDebug("Cached {Count} Genius songs for query: '{Query}'", songs.Count, normalizedQuery);

            return songs;
        }
        catch (JsonException jsonEx)
        {
            logger.LogError(jsonEx, "Failed to deserialize Genius API response for query '{Query}'", searchQuery);
            return null;
        }
        catch (HttpRequestException httpEx)
        {
            logger.LogError(httpEx, "HTTP request to Genius API failed for query '{Query}'", searchQuery);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An unexpected error occurred while searching Genius for query '{Query}'",
                searchQuery);
            return null;
        }
    }

    public async Task<GeniusSong?> GetSongByIdAsync(long songId)
    {
        if (string.IsNullOrWhiteSpace(config.GeniusToken))
        {
            logger.LogError("Genius API token is not configured. Cannot fetch song by ID.");
            return null;
        }

        var cacheKey = $"{SongCacheKeyPrefix}{songId}";

        if (memoryCache.TryGetValue(cacheKey, out GeniusSong? cachedSong) && cachedSong != null)
        {
            logger.LogDebug("Genius song ID cache hit for: {SongId}", songId);
            return cachedSong;
        }

        logger.LogDebug("Genius song ID cache miss for: {SongId}. Fetching from API.", songId);

        var requestUrl = $"{GeniusApiBaseUrl}/songs/{songId}";

        var httpClient = httpClientFactory.CreateClient(HttpClientName);
        var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.GeniusToken);
        request.Headers.UserAgent.ParseAdd(UserAgent);

        try
        {
            var response = await httpClient.SendAsync(request).ConfigureAwait(false);
            var responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError(
                    "Genius API request for song ID {SongId} failed with status {StatusCode}. Response: {Response}",
                    songId, response.StatusCode,
                    responseContent.Length > 500 ? responseContent[..500] : responseContent);
                return null;
            }

            var geniusResponse = JsonConvert.DeserializeObject<GeniusSongResponse>(responseContent);

            if (geniusResponse?.Meta.Status != 200 || geniusResponse.Response?.Song == null)
            {
                logger.LogWarning(
                    "Genius API returned non-200 status or invalid response structure for song ID {SongId}. Meta Status: {MetaStatus}",
                    songId, geniusResponse?.Meta.Status);
                return null;
            }

            var song = geniusResponse.Response.Song;
            var cacheEntryOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(CacheDuration)
                .SetSize(1);

            memoryCache.Set(cacheKey, song, cacheEntryOptions);
            logger.LogDebug("Cached Genius song for ID: {SongId}", songId);

            return song;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while fetching Genius song ID {SongId}", songId);
            return null;
        }
    }

    public GeniusSong? GetSongFromCache(long songId)
    {
        var cacheKey = $"{SongCacheKeyPrefix}{songId}";
        if (memoryCache.TryGetValue(cacheKey, out GeniusSong? cachedSong) && cachedSong != null)
        {
            logger.LogDebug("Genius song ID cache hit (cache-only) for: {SongId}", songId);
            return cachedSong;
        }

        logger.LogDebug("Genius song ID cache miss (cache-only) for: {SongId}", songId);
        return null;
    }

    public async Task<string?> GetLyricsFromPathAsync(long songId, string songPath, bool fetchIfNotInCache = true)
    {
        if (string.IsNullOrWhiteSpace(songPath))
        {
            logger.LogWarning("Cannot fetch lyrics: songPath is empty for songId {SongId}.", songId);
            return null;
        }

        var cacheKey = $"{LyricsCacheKeyPrefix}{songId}";
        if (memoryCache.TryGetValue(cacheKey, out string? cachedLyrics))
        {
            logger.LogDebug("Lyrics cache hit for songId: {SongId}", songId);
            return cachedLyrics;
        }

        if (!fetchIfNotInCache)
        {
            logger.LogDebug("Lyrics cache miss for songId {SongId} and fetchIfNotInCache is false. Returning null.",
                songId);
            return null;
        }

        logger.LogDebug("Lyrics cache miss for songId: {SongId}. Fetching from Genius page.", songId);

        var songUrl = $"{GeniusBaseUrl}/{songPath.TrimStart('/')}";
        var httpClient = httpClientFactory.CreateClient(HttpClientName);
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);

        string htmlRaw;
        try
        {
            htmlRaw = await httpClient.GetStringAsync(songUrl).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Failed to fetch HTML for lyrics from URL: {SongUrl}", songUrl);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error fetching HTML for lyrics from URL: {SongUrl}", songUrl);
            return null;
        }

        if (string.IsNullOrWhiteSpace(htmlRaw))
        {
            logger.LogWarning("Fetched HTML for lyrics is empty from URL: {SongUrl}", songUrl);
            return null;
        }

        htmlRaw = htmlRaw.Replace("<br/>", "\n").Replace("<br>", "\n");

        var doc = new HtmlDocument();
        doc.LoadHtml(htmlRaw);

        var divs = doc.DocumentNode
            .Descendants("div")
            .Where(div =>
                div.Attributes.Contains("data-lyrics-container"))
            .ToList();

        if (divs.Count == 0)
        {
            logger.LogWarning(
                "Could not find lyrics section in HTML for songId {SongId}, URL: {SongUrl}.",
                songId, songUrl);
            return null;
        }

        var lyrics = string.Join("\n\n", divs.Select(d =>
        {
            var copy = d.Clone();
            var firstChild = copy.ChildNodes
                .FirstOrDefault(n => n.Name == "div");
            if (firstChild != null &&
                firstChild.GetAttributeValue("data-exclude-from-selection", "") == "true")
                copy.RemoveChild(firstChild);
            return HtmlEntity.DeEntitize(copy.InnerText);
        })).Trim('\n');

        memoryCache.Set(cacheKey, lyrics, CacheDuration);
        return lyrics;
    }
}