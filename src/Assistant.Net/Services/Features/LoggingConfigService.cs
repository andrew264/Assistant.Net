using System.Collections.Concurrent;
using Assistant.Net.Data.Entities;
using Assistant.Net.Data.Enums;
using Assistant.Net.Data.Repositories.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Services.Features;

public class LoggingConfigService(
    IUnitOfWorkFactory uowFactory,
    IMemoryCache memoryCache,
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

            await using var uow = await uowFactory.CreateAsync().ConfigureAwait(false);

            var config = await uow.Logging.GetAsync(guildId, logType).ConfigureAwait(false) ?? new LogSettingsEntity
            {
                GuildId = guildId,
                LogType = logType,
                IsEnabled = false,
                DeleteDelayMs = 86400000 // 24 hours
            };

            memoryCache.Set(cacheKey, config, CacheDuration);
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
        await using var uow = await uowFactory.CreateAsync().ConfigureAwait(false);

        await uow.Guilds.EnsureExistsAsync(config.GuildId).ConfigureAwait(false);

        var affected = await uow.Logging.ExecuteUpdateAsync(
            config.GuildId,
            config.LogType,
            config.IsEnabled,
            config.ChannelId,
            config.DeleteDelayMs,
            config.UpdatedAt).ConfigureAwait(false);

        if (affected == 0) uow.Logging.Add(config);

        await uow.SaveChangesAsync().ConfigureAwait(false);

        var cacheKey = $"{CachePrefix}{config.GuildId}:{config.LogType}";
        memoryCache.Set(cacheKey, config, CacheDuration);

        logger.LogInformation("Updated log config for Guild {GuildId}, Type {LogType}", config.GuildId, config.LogType);
    }
}