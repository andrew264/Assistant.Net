using System.Collections.Concurrent;
using Assistant.Net.Data.Entities;
using Assistant.Net.Data.Repositories.Interfaces;
using Assistant.Net.Models.Music;
using Assistant.Net.Options;
using Lavalink4NET.Players;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Net.Services.Data;

public class MusicHistoryService
{
    private const string SettingsCachePrefix = "music:settings:";
    private static readonly TimeSpan SettingsCacheDuration = TimeSpan.FromHours(2);
    private readonly ILogger<MusicHistoryService> _logger;
    private readonly IMemoryCache _memoryCache;
    private readonly MusicOptions _musicOptions;
    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _settingsLocks = new();

    private readonly IUnitOfWorkFactory _uowFactory;

    public MusicHistoryService(
        IUnitOfWorkFactory uowFactory,
        ILogger<MusicHistoryService> logger,
        IOptions<MusicOptions> musicOptions,
        IMemoryCache memoryCache)
    {
        _uowFactory = uowFactory;
        _logger = logger;
        _musicOptions = musicOptions.Value;
        _memoryCache = memoryCache;

        _logger.LogInformation("MusicHistoryService initialized.");
    }

    private async Task<GuildMusicSettingsEntity> GetSettingsAsync(ulong guildId)
    {
        var cacheKey = $"{SettingsCachePrefix}{guildId}";

        if (_memoryCache.TryGetValue(cacheKey, out GuildMusicSettingsEntity? cachedSettings) && cachedSettings != null)
            return cachedSettings;

        var guildLock = _settingsLocks.GetOrAdd(guildId, _ => new SemaphoreSlim(1, 1));
        await guildLock.WaitAsync().ConfigureAwait(false);

        try
        {
            if (_memoryCache.TryGetValue(cacheKey, out cachedSettings) && cachedSettings != null) return cachedSettings;

            _logger.LogTrace("Music settings cache miss for Guild {GuildId}. Fetching from DB.", guildId);
            await using var uow = await _uowFactory.CreateAsync().ConfigureAwait(false);

            var settings = await uow.Music.GetGuildSettingsAsync(guildId).ConfigureAwait(false);

            if (settings == null)
            {
                await uow.Guilds.EnsureExistsAsync(guildId).ConfigureAwait(false);
                settings = new GuildMusicSettingsEntity
                {
                    GuildId = guildId,
                    Volume = _musicOptions.DefaultVolume
                };
                uow.Music.AddGuildSettings(settings);
                await uow.SaveChangesAsync().ConfigureAwait(false);
                _logger.LogInformation("Created default music settings for Guild {GuildId}", guildId);
            }

            var cacheEntryOptions = new MemoryCacheEntryOptions()
                .SetSlidingExpiration(SettingsCacheDuration)
                .SetSize(1);
            _memoryCache.Set(cacheKey, settings, cacheEntryOptions);

            return settings;
        }
        finally
        {
            guildLock.Release();
            _settingsLocks.TryRemove(guildId, out _);
        }
    }

    public async Task AddSongToHistoryAsync(ulong guildId, ITrackQueueItem queueItem)
    {
        if (queueItem.Track?.Uri is null)
        {
            _logger.LogWarning("Cannot add track to history: Track or URI is null. Guild: {GuildId}", guildId);
            return;
        }

        await GetSettingsAsync(guildId).ConfigureAwait(false);

        var requesterId = 0UL;
        var customQueueItem = queueItem.As<CustomTrackQueueItem>();
        if (customQueueItem is not null)
            requesterId = customQueueItem.RequesterId;
        else
            _logger.LogWarning(
                "Could not determine requester for track '{TrackTitle}' in guild {GuildId}. The queue item was not a CustomTrackQueueItem.",
                queueItem.Track.Title, guildId);

        await using var uow = await _uowFactory.CreateAsync().ConfigureAwait(false);

        try
        {
            await uow.Users.EnsureExistsAsync(requesterId).ConfigureAwait(false);
            await uow.Guilds.EnsureExistsAsync(guildId).ConfigureAwait(false);

            var trackUri = queueItem.Track.Uri.ToString();
            var track = await uow.Music.GetTrackByUriAsync(trackUri).ConfigureAwait(false);
            if (track == null)
            {
                track = new TrackEntity
                {
                    Uri = trackUri,
                    Title = queueItem.Track.Title,
                    Artist = queueItem.Track.Author,
                    ThumbnailUrl = queueItem.Track.ArtworkUri?.ToString(),
                    Duration = queueItem.Track.Duration.TotalSeconds,
                    Source = queueItem.Track.SourceName ?? "unknown"
                };
                uow.Music.AddTrack(track);
            }

            var playHistory = new PlayHistoryEntity
            {
                GuildId = guildId,
                Track = track,
                RequestedBy = requesterId,
                PlayedAt = DateTime.UtcNow
            };
            uow.Music.AddPlayHistory(playHistory);

            await uow.SaveChangesAsync().ConfigureAwait(false);
            _logger.LogDebug("Added song '{SongTitle}' to history for Guild {GuildId}.", queueItem.Track.Title,
                guildId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add song to history for Guild {GuildId}.", guildId);
        }
    }

    public async Task<List<TrackPlayCount>> GetTopPlaysAsync(ulong guildId, ulong? requesterId = null, int limit = 10)
    {
        await using var uow = await _uowFactory.CreateAsync().ConfigureAwait(false);
        var result = await uow.Music.GetTopPlaysAsync(guildId, requesterId, limit).ConfigureAwait(false);
        return result.Select(x => new TrackPlayCount(x.Track, x.Count)).ToList();
    }

    public async Task<IEnumerable<TrackEntity>> SearchSongHistoryAsync(string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm)) return [];
        await using var uow = await _uowFactory.CreateAsync().ConfigureAwait(false);
        return await uow.Music.SearchTracksAsync(searchTerm, _musicOptions.TitleSimilarityCutoff, 24)
            .ConfigureAwait(false);
    }

    public async Task<float> GetGuildVolumeAsync(ulong guildId)
    {
        var settings = await GetSettingsAsync(guildId).ConfigureAwait(false);
        return settings.Volume;
    }

    public async Task SetGuildVolumeAsync(ulong guildId, double volume)
    {
        var clampedVolume = (float)Math.Clamp(volume, 0.0, 2.0);
        var cacheKey = $"{SettingsCachePrefix}{guildId}";

        await using var uow = await _uowFactory.CreateAsync().ConfigureAwait(false);

        var settings = await uow.Music.GetGuildSettingsAsync(guildId).ConfigureAwait(false);
        if (settings == null)
        {
            await uow.Guilds.EnsureExistsAsync(guildId).ConfigureAwait(false);
            settings = new GuildMusicSettingsEntity { GuildId = guildId, Volume = clampedVolume };
            uow.Music.AddGuildSettings(settings);
        }
        else
        {
            settings.Volume = clampedVolume;
        }

        try
        {
            await uow.SaveChangesAsync().ConfigureAwait(false);
            _logger.LogInformation("Set volume for Guild {GuildId} to {Volume}%.", guildId, clampedVolume * 100);

            var cacheEntryOptions = new MemoryCacheEntryOptions()
                .SetSlidingExpiration(SettingsCacheDuration)
                .SetSize(1);
            _memoryCache.Set(cacheKey, settings, cacheEntryOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set volume for Guild {GuildId}.", guildId);
        }
    }
}