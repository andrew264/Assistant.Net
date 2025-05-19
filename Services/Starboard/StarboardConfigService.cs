using System.Collections.Concurrent;
using Assistant.Net.Models.Starboard;
using Assistant.Net.Utilities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Assistant.Net.Services.Starboard;

public class StarboardConfigService
{
    private const string CachePrefix = "starboardConfig:";
    private const int CacheSize = 1000;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);
    private readonly ConcurrentQueue<ulong> _cacheKeysQueue = new();

    private readonly IMongoCollection<StarboardConfigModel> _configCollection;
    private readonly ILogger<StarboardConfigService> _logger;
    private readonly IMemoryCache _memoryCache;

    public StarboardConfigService(IMongoDatabase database, IMemoryCache memoryCache,
        ILogger<StarboardConfigService> logger)
    {
        _configCollection = database.GetCollection<StarboardConfigModel>("starboardConfigs");
        _memoryCache = memoryCache;
        _logger = logger;

        _logger.LogInformation("StarboardConfigService initialized.");
    }

    public async Task<StarboardConfigModel> GetGuildConfigAsync(ulong guildId)
    {
        var cacheKey = $"{CachePrefix}{guildId}";

        if (_memoryCache.TryGetValue(cacheKey, out StarboardConfigModel? cachedConfig) && cachedConfig != null)
        {
            _logger.LogTrace("Starboard config cache hit for Guild {GuildId}", guildId);
            return cachedConfig;
        }

        _logger.LogTrace("Starboard config cache miss for Guild {GuildId}", guildId);
        var config = await _configCollection.Find(x => x.GuildId == guildId).FirstOrDefaultAsync().ConfigureAwait(false);

        config ??= new StarboardConfigModel { GuildId = guildId };

        AddToCache(cacheKey, config);

        return config;
    }

    public async Task UpdateConfigAsync(StarboardConfigModel config)
    {
        config.UpdatedAt = DateTime.UtcNow;

        var filter = Builders<StarboardConfigModel>.Filter.Eq(x => x.GuildId, config.GuildId);
        var options = new ReplaceOptions { IsUpsert = true };

        await _configCollection.ReplaceOneAsync(filter, config, options).ConfigureAwait(false);

        var cacheKey = $"{CachePrefix}{config.GuildId}";
        AddToCache(cacheKey, config);

        _logger.LogInformation("Updated starboard config for Guild {GuildId}", config.GuildId);
    }

    private void AddToCache(string key, StarboardConfigModel config)
    {
        var cacheEntryOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(CacheDuration)
            .SetSize(1);

        _memoryCache.Set(key, config, cacheEntryOptions);
        _cacheKeysQueue.Enqueue(config.GuildId);

        while (_cacheKeysQueue.Count > CacheSize && _cacheKeysQueue.TryDequeue(out var oldestGuildId))
            _memoryCache.Remove($"{CachePrefix}{oldestGuildId}");
    }

    public static bool IsValidEmoji(string emoji)
    {
        if (string.IsNullOrWhiteSpace(emoji)) return false;

        if (char.IsSurrogatePair(emoji, 0) || emoji.Length == 1) return true;

        return RegexPatterns.DiscordEmoji().IsMatch(emoji);
    }
}