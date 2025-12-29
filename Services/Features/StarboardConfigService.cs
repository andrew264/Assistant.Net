using System.Collections.Concurrent;
using Assistant.Net.Data;
using Assistant.Net.Data.Entities;
using Assistant.Net.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Services.Features;

public class StarboardConfigService
{
    private const string CachePrefix = "starboardConfig:";
    private const int CacheSize = 1000;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);
    private readonly ConcurrentQueue<ulong> _cacheKeysQueue = new();

    private readonly IDbContextFactory<AssistantDbContext> _dbFactory;
    private readonly ILogger<StarboardConfigService> _logger;
    private readonly IMemoryCache _memoryCache;

    public StarboardConfigService(IDbContextFactory<AssistantDbContext> dbFactory, IMemoryCache memoryCache,
        ILogger<StarboardConfigService> logger)
    {
        _dbFactory = dbFactory;
        _memoryCache = memoryCache;
        _logger = logger;

        _logger.LogInformation("StarboardConfigService initialized.");
    }

    public async Task<StarboardConfigEntity> GetGuildConfigAsync(ulong guildId)
    {
        var cacheKey = $"{CachePrefix}{guildId}";

        if (_memoryCache.TryGetValue(cacheKey, out StarboardConfigEntity? cachedConfig) && cachedConfig != null)
        {
            _logger.LogTrace("Starboard config cache hit for Guild {GuildId}", guildId);
            return cachedConfig;
        }

        _logger.LogTrace("Starboard config cache miss for Guild {GuildId}", guildId);

        await using var context = await _dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        var decimalGuildId = (decimal)guildId;

        var config = await context.StarboardConfigs
            .FindAsync(decimalGuildId)
            .ConfigureAwait(false);

        config ??= new StarboardConfigEntity { GuildId = decimalGuildId };

        AddToCache(cacheKey, config);

        return config;
    }

    public async Task UpdateConfigAsync(StarboardConfigEntity config)
    {
        config.UpdatedAt = DateTime.UtcNow;

        await using var context = await _dbFactory.CreateDbContextAsync().ConfigureAwait(false);

        var existing = await context.StarboardConfigs.FindAsync(config.GuildId).ConfigureAwait(false);
        if (existing == null)
        {
            config.CreatedAt = DateTime.UtcNow;
            context.StarboardConfigs.Add(config);
        }
        else
        {
            context.Entry(existing).CurrentValues.SetValues(config);
        }

        await context.SaveChangesAsync().ConfigureAwait(false);

        var cacheKey = $"{CachePrefix}{config.GuildId}";
        AddToCache(cacheKey, config);

        _logger.LogInformation("Updated starboard config for Guild {GuildId}", config.GuildId);
    }

    private void AddToCache(string key, StarboardConfigEntity config)
    {
        var cacheEntryOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(CacheDuration)
            .SetSize(1);

        _memoryCache.Set(key, config, cacheEntryOptions);
        _cacheKeysQueue.Enqueue((ulong)config.GuildId);

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