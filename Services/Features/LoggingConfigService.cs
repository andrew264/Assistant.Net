using System.Collections.Concurrent;
using Assistant.Net.Data;
using Assistant.Net.Data.Entities;
using Assistant.Net.Data.Enums;
using Assistant.Net.Services.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Services.Features;

public class LoggingConfigService(
    IDbContextFactory<AssistantDbContext> dbFactory,
    IMemoryCache memoryCache,
    GuildService guildService,
    ILogger<LoggingConfigService> logger)
{
    private const string CachePrefix = "log_config:";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public async Task<LogSettingsEntity> GetLogConfigAsync(ulong guildId, LogType logType)
    {
        var cacheKey = $"{CachePrefix}{guildId}:{logType}";

        if (memoryCache.TryGetValue(cacheKey, out LogSettingsEntity? cachedConfig) && cachedConfig != null)
            return cachedConfig;

        var lockObj = _locks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
        await lockObj.WaitAsync().ConfigureAwait(false);

        try
        {
            if (memoryCache.TryGetValue(cacheKey, out cachedConfig) && cachedConfig != null) return cachedConfig;

            await using var context = await dbFactory.CreateDbContextAsync().ConfigureAwait(false);
            var dGuildId = (decimal)guildId;

            var config = await context.LogSettings
                .FirstOrDefaultAsync(l => l.GuildId == dGuildId && l.LogType == logType)
                .ConfigureAwait(false) ?? new LogSettingsEntity
            {
                GuildId = dGuildId,
                LogType = logType,
                IsEnabled = false,
                DeleteDelayMs = 86400000 // 24 hours
            };

            var cacheEntryOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(CacheDuration);

            memoryCache.Set(cacheKey, config, cacheEntryOptions);
            return config;
        }
        finally
        {
            lockObj.Release();
            _locks.TryRemove(cacheKey, out _);
        }
    }

    public async Task UpdateLogConfigAsync(LogSettingsEntity config)
    {
        await using var context = await dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        var guildId = (ulong)config.GuildId;

        await guildService.EnsureGuildExistsAsync(context, guildId).ConfigureAwait(false);

        var existing = await context.LogSettings
            .FirstOrDefaultAsync(l => l.GuildId == config.GuildId && l.LogType == config.LogType)
            .ConfigureAwait(false);

        config.UpdatedAt = DateTime.UtcNow;

        if (existing == null)
        {
            context.LogSettings.Add(config);
        }
        else
        {
            existing.IsEnabled = config.IsEnabled;
            existing.ChannelId = config.ChannelId;
            existing.DeleteDelayMs = config.DeleteDelayMs;
            existing.UpdatedAt = config.UpdatedAt;
            context.Entry(existing).State = EntityState.Modified;
        }

        await context.SaveChangesAsync().ConfigureAwait(false);

        var cacheKey = $"{CachePrefix}{guildId}:{config.LogType}";
        memoryCache.Set(cacheKey, existing ?? config, CacheDuration);

        logger.LogInformation("Updated log config for Guild {GuildId}, Type {LogType}", guildId, config.LogType);
    }
}