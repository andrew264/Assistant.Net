using System.Collections.Concurrent;
using Assistant.Net.Data;
using Assistant.Net.Data.Entities;
using Assistant.Net.Models.Music;
using Assistant.Net.Options;
using Lavalink4NET.Players;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Assistant.Net.Services.Data;

public class MusicHistoryService
{
    private const string SettingsCachePrefix = "music:settings:";
    private static readonly TimeSpan SettingsCacheDuration = TimeSpan.FromHours(2);

    private readonly IDbContextFactory<AssistantDbContext> _dbFactory;
    private readonly GuildService _guildService;
    private readonly ILogger<MusicHistoryService> _logger;
    private readonly IMemoryCache _memoryCache;
    private readonly MusicOptions _musicOptions;
    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _settingsLocks = new();
    private readonly UserService _userService;

    public MusicHistoryService(
        IDbContextFactory<AssistantDbContext> dbFactory,
        ILogger<MusicHistoryService> logger,
        IOptions<MusicOptions> musicOptions,
        UserService userService, GuildService guildService, IMemoryCache memoryCache)
    {
        _dbFactory = dbFactory;
        _logger = logger;
        _musicOptions = musicOptions.Value;
        _userService = userService;
        _guildService = guildService;
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
            await using var context = await _dbFactory.CreateDbContextAsync().ConfigureAwait(false);
            var decimalGuildId = (decimal)guildId;

            var settings = await context.GuildMusicSettings.FindAsync(decimalGuildId).ConfigureAwait(false);

            if (settings == null)
            {
                await _guildService.EnsureGuildExistsAsync(context, guildId).ConfigureAwait(false);
                settings = new GuildMusicSettingsEntity
                {
                    GuildId = decimalGuildId,
                    Volume = _musicOptions.DefaultVolume
                };
                context.GuildMusicSettings.Add(settings);
                await context.SaveChangesAsync().ConfigureAwait(false);
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

        await using var context = await _dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        var decimalGuildId = (decimal)guildId;
        var decimalRequesterId = (decimal)requesterId;

        try
        {
            await _userService.EnsureUserExistsAsync(context, requesterId).ConfigureAwait(false);

            var trackUri = queueItem.Track.Uri.ToString();
            var track = await context.Tracks.FirstOrDefaultAsync(t => t.Uri == trackUri).ConfigureAwait(false);
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
                context.Tracks.Add(track);
            }

            var playHistory = new PlayHistoryEntity
            {
                GuildId = decimalGuildId,
                Track = track,
                RequestedBy = decimalRequesterId,
                PlayedAt = DateTime.UtcNow
            };
            context.PlayHistories.Add(playHistory);

            await context.SaveChangesAsync().ConfigureAwait(false);
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
        await using var context = await _dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        var decimalGuildId = (decimal)guildId;

        var query = context.PlayHistories
            .Include(ph => ph.Track)
            .Where(ph => ph.GuildId == decimalGuildId);

        if (requesterId.HasValue)
        {
            var decimalRequesterId = (decimal)requesterId.Value;
            query = query.Where(ph => ph.RequestedBy == decimalRequesterId);
        }

        var result = await query
            .GroupBy(ph => ph.Track)
            .Select(g => new { Track = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(limit)
            .ToListAsync()
            .ConfigureAwait(false);

        return result.Select(x => new TrackPlayCount(x.Track, x.Count)).ToList();
    }

    public async Task<IEnumerable<TrackEntity>> SearchSongHistoryAsync(string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm)) return [];

        await using var context = await _dbFactory.CreateDbContextAsync().ConfigureAwait(false);

        if (searchTerm.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
            searchTerm.StartsWith("www", StringComparison.OrdinalIgnoreCase))
            return await context.Tracks
                .Where(t => EF.Functions.ILike(t.Uri, $"%{searchTerm}%"))
                .OrderBy(t => t.Title)
                .Take(24)
                .Select(t => new TrackEntity
                {
                    Title = t.Title,
                    Uri = t.Uri
                })
                .ToListAsync()
                .ConfigureAwait(false);

        var cutoff = _musicOptions.TitleSimilarityCutoff;

        var tracks = await context.Tracks
            .Where(t => EF.Functions.TrigramsSimilarity(t.Title, searchTerm) > cutoff ||
                        EF.Functions.ILike(t.Title, $"%{searchTerm}%"))
            .OrderByDescending(t => t.Title)
            .Take(24)
            .Select(t => new TrackEntity
            {
                Title = t.Title,
                Uri = t.Uri
            })
            .ToListAsync();

        return tracks;
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

        await using var context = await _dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        var decimalGuildId = (decimal)guildId;

        var settings = await context.GuildMusicSettings.FindAsync(decimalGuildId).ConfigureAwait(false);
        if (settings == null)
        {
            await _guildService.EnsureGuildExistsAsync(context, guildId).ConfigureAwait(false);
            settings = new GuildMusicSettingsEntity { GuildId = decimalGuildId, Volume = clampedVolume };
            context.GuildMusicSettings.Add(settings);
        }
        else
        {
            settings.Volume = clampedVolume;
        }

        try
        {
            await context.SaveChangesAsync().ConfigureAwait(false);
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