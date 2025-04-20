using System.Web;
using Assistant.Net.Models.UrbanDictionary;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Assistant.Net.Services;

public class UrbanDictionaryService(
    IHttpClientFactory httpClientFactory,
    IMemoryCache memoryCache,
    ILogger<UrbanDictionaryService> logger)
{
    private const string UdApiRandom = "https://api.urbandictionary.com/v0/random";
    private const string UdApiDefine = "https://api.urbandictionary.com/v0/define?term={0}";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(3); // 3-hour cache

    public async Task<List<UrbanDictionaryEntry>?> GetDefinitionsAsync(string? term)
    {
        var searchTerm = term?.Trim();
        var isRandom = string.IsNullOrEmpty(searchTerm);
        var cacheKey = isRandom ? "urban:random" : $"urban:{searchTerm}";

        // Try fetching from cache
        if (!isRandom && memoryCache.TryGetValue(cacheKey, out List<UrbanDictionaryEntry>? cachedResults))
        {
            logger.LogDebug("Cache hit for Urban Dictionary term: {Term}", searchTerm);
            return cachedResults;
        }

        var apiUrl = isRandom ? UdApiRandom : string.Format(UdApiDefine, HttpUtility.UrlEncode(searchTerm));
        logger.LogDebug("Fetching Urban Dictionary definition from API: {Url}", apiUrl);

        try
        {
            var httpClient = httpClientFactory.CreateClient("UrbanDictionary");
            var response = await httpClient.GetAsync(apiUrl);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Urban Dictionary API request failed with status code {StatusCode} for URL: {Url}",
                    response.StatusCode, apiUrl);
                return null;
            }

            var jsonString = await response.Content.ReadAsStringAsync();
            var apiResponse = JsonConvert.DeserializeObject<UrbanDictionaryResponse>(jsonString);

            if (apiResponse?.List == null || apiResponse.List.Count == 0)
            {
                logger.LogInformation("No Urban Dictionary definitions found for term: {Term}",
                    searchTerm ?? "[Random]");
                if (!isRandom) memoryCache.Set(cacheKey, new List<UrbanDictionaryEntry>(), CacheDuration);
                return [];
            }

            // Sort results by thumbs up, descending (most popular first)
            var sortedResults = apiResponse.List.OrderByDescending(e => e.ThumbsUp).ToList();

            if (!isRandom)
            {
                logger.LogDebug("Cache miss for Urban Dictionary term: {Term}. Caching {Count} results.", searchTerm,
                    sortedResults.Count);
                memoryCache.Set(cacheKey, sortedResults, CacheDuration);
            }
            else
            {
                logger.LogDebug("Fetched {Count} random Urban Dictionary definitions.", sortedResults.Count);
            }

            return sortedResults;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HTTP error occurred while fetching Urban Dictionary definition for: {Term}",
                searchTerm ?? "[Random]");
            return null;
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "JSON error occurred while parsing Urban Dictionary response for: {Term}",
                searchTerm ?? "[Random]");
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error occurred while fetching Urban Dictionary definition for: {Term}",
                searchTerm ?? "[Random]");
            return null;
        }
    }
}