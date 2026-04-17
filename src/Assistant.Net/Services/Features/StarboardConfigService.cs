using System.Collections.Concurrent;
using Assistant.Net.Data.Entities;
using Assistant.Net.Data.Repositories.Interfaces;
using Assistant.Net.Utilities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Services.Features;

public class StarboardConfigService(
    IUnitOfWorkFactory uowFactory,
    IMemoryCache memoryCache,
    ILogger<StarboardConfigService> logger)
{
    private const string CachePrefix = "starboardConfig:";
    private const int CacheSize = 1000;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);
    private readonly ConcurrentQueue<ulong> _cacheKeysQueue = new();

    public async Task<StarboardConfigEntity> GetGuildConfigAsync(ulong guildId)
    {
        var cacheKey = $"{CachePrefix}{guildId}";

        if (memoryCache.TryGetValue(cacheKey, out StarboardConfigEntity? cachedConfig) && cachedConfig != null)
        {
            logger.LogTrace("Starboard config cache hit for Guild {GuildId}", guildId);
            return cachedConfig;
        }

        logger.LogTrace("Starboard config cache miss for Guild {GuildId}", guildId);

        await using var uow = await uowFactory.CreateAsync().ConfigureAwait(false);

        var config = await uow.Starboard.GetConfigAsync(guildId).ConfigureAwait(false);
        config ??= new StarboardConfigEntity { GuildId = guildId };

        AddToCache(cacheKey, config);

        return config;
    }

    public async Task UpdateConfigAsync(StarboardConfigEntity config)
    {
        config.UpdatedAt = DateTime.UtcNow;

        await using var uow = await uowFactory.CreateAsync().ConfigureAwait(false);
        await uow.Guilds.EnsureExistsAsync(config.GuildId).ConfigureAwait(false);

        var existing = await uow.Starboard.GetConfigAsync(config.GuildId).ConfigureAwait(false);
        if (existing == null)
        {
            config.CreatedAt = DateTime.UtcNow;
            uow.Starboard.AddConfig(config);
        }
        else
        {
            existing.IsEnabled = config.IsEnabled;
            existing.StarboardChannelId = config.StarboardChannelId;
            existing.StarEmoji = config.StarEmoji;
            existing.Threshold = config.Threshold;
            existing.AllowSelfStar = config.AllowSelfStar;
            existing.AllowBotMessages = config.AllowBotMessages;
            existing.IgnoreNsfwChannels = config.IgnoreNsfwChannels;
            existing.DeleteIfUnStarred = config.DeleteIfUnStarred;
            existing.LogChannelId = config.LogChannelId;
            existing.UpdatedAt = config.UpdatedAt;
        }

        await uow.SaveChangesAsync().ConfigureAwait(false);

        var cacheKey = $"{CachePrefix}{config.GuildId}";
        AddToCache(cacheKey, config);

        logger.LogInformation("Updated starboard config for Guild {GuildId}", config.GuildId);
    }

    private void AddToCache(string key, StarboardConfigEntity config)
    {
        var cacheEntryOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(CacheDuration)
            .SetSize(1);

        memoryCache.Set(key, config, cacheEntryOptions);
        _cacheKeysQueue.Enqueue(config.GuildId);

        while (_cacheKeysQueue.Count > CacheSize && _cacheKeysQueue.TryDequeue(out var oldestGuildId))
            memoryCache.Remove($"{CachePrefix}{oldestGuildId}");
    }

    public static bool IsValidEmoji(string emoji)
    {
        if (string.IsNullOrWhiteSpace(emoji)) return false;
        if (char.IsSurrogatePair(emoji, 0) || emoji.Length == 1) return true;
        return RegexPatterns.DiscordEmoji().IsMatch(emoji);
    }
}