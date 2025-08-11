using System.Collections.Concurrent;
using Assistant.Net.Configuration;
using Assistant.Net.Models.Music;
using F23.StringSimilarity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Assistant.Net.Services.Music;

public class MusicHistoryService
{
    private const string CollectionName = "guildMusicSettings";
    private const string CachePrefix = "musicHistory:";
    private const int CacheSizeLimit = 500;
    private readonly ConcurrentQueue<ulong> _cacheKeysQueue = new();
    private readonly ILogger<MusicHistoryService> _logger;
    private readonly IMemoryCache _memoryCache;
    private readonly MusicConfig _musicConfig;

    private readonly IMongoCollection<GuildMusicSettingsModel> _settingsCollection;

    public MusicHistoryService(
        IMongoDatabase database,
        IMemoryCache memoryCache,
        ILogger<MusicHistoryService> logger,
        Config config)
    {
        _settingsCollection = database.GetCollection<GuildMusicSettingsModel>(CollectionName);
        _memoryCache = memoryCache;
        _logger = logger;
        _musicConfig = config.Music;

        EnsureIndexesAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        _logger.LogInformation("MusicHistoryService initialized.");
    }

    private static async Task EnsureIndexesAsync()
    {
        // If specific queries on song properties become frequent, consider indexing `songs.uri` or `songs.title`.

        // var songsUriIndexModel = new CreateIndexModel<GuildMusicSettingsModel>(
        //     Builders<GuildMusicSettingsModel>.IndexKeys.Ascending("songs.uri"),
        //     new CreateIndexOptions { Name = "SongsUriIndex", Sparse = true }
        // );
        // await _settingsCollection.Indexes.CreateOneAsync(songsUriIndexModel);
        // _logger.LogInformation("Music history index check complete (primary _id index used).");
        await Task.CompletedTask;
    }

    private async Task<GuildMusicSettingsModel> GetOrAddSettingsAsync(ulong guildId)
    {
        var cacheKey = $"{CachePrefix}{guildId}";

        if (_memoryCache.TryGetValue(cacheKey, out GuildMusicSettingsModel? cachedSettings) && cachedSettings != null)
        {
            _logger.LogTrace("Music history cache hit for Guild {GuildId}", guildId);
            return cachedSettings;
        }

        _logger.LogTrace("Music history cache miss for Guild {GuildId}. Fetching from DB.", guildId);
        var settings = await _settingsCollection.Find(x => x.GuildId == guildId).FirstOrDefaultAsync()
            .ConfigureAwait(false);

        if (settings == null)
        {
            _logger.LogInformation("No music settings found for Guild {GuildId}. Creating with defaults.", guildId);
            settings = new GuildMusicSettingsModel
            {
                GuildId = guildId,
                Volume = _musicConfig.DefaultVolume,
                Songs = [],
                UpdatedAt = DateTime.UtcNow
            };
        }

        AddToCache(cacheKey, settings);
        return settings;
    }

    private void AddToCache(string cacheKey, GuildMusicSettingsModel settings)
    {
        var cacheEntryOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(4),
            SlidingExpiration = TimeSpan.FromHours(1),
            Size = 1
        };

        _memoryCache.Set(cacheKey, settings, cacheEntryOptions);

        // Manage cache size
        if (_cacheKeysQueue.Contains(settings.GuildId)) return; // Avoid duplicate guild IDs in queue
        _cacheKeysQueue.Enqueue(settings.GuildId);
        while (_cacheKeysQueue.Count > CacheSizeLimit && _cacheKeysQueue.TryDequeue(out var oldestGuildId))
        {
            _memoryCache.Remove($"{CachePrefix}{oldestGuildId}");
            _logger.LogDebug("Evicted music history for Guild {GuildId} from cache due to size limit.", oldestGuildId);
        }
    }

    public async Task AddSongToHistoryAsync(ulong guildId, SongHistoryEntry songEntry)
    {
        try
        {
            var filter = Builders<GuildMusicSettingsModel>.Filter.Eq(x => x.GuildId, guildId);
            var update = Builders<GuildMusicSettingsModel>.Update
                .PushEach("songs", [songEntry], _musicConfig.MaxHistorySize, 0)
                .SetOnInsert(x => x.GuildId, guildId) // Ensure GuildId is set on insert
                .SetOnInsert(x => x.Volume, _musicConfig.DefaultVolume) // Set default volume on insert
                .Set(x => x.UpdatedAt, DateTime.UtcNow);

            var options = new UpdateOptions { IsUpsert = true };
            await _settingsCollection.UpdateOneAsync(filter, update, options).ConfigureAwait(false);

            _logger.LogDebug("Added song '{SongTitle}' to history for Guild {GuildId}.", songEntry.Title, guildId);

            // Invalidate and refetch cache to ensure consistency
            var cacheKey = $"{CachePrefix}{guildId}";
            _memoryCache.Remove(cacheKey);
            await GetOrAddSettingsAsync(guildId).ConfigureAwait(false); // Re-cache
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add song to history for Guild {GuildId}.", guildId);
        }
    }

    public async Task<IEnumerable<SongHistoryEntryInfo>> SearchSongHistoryAsync(ulong guildId, string searchTerm)
    {
        var settings = await GetOrAddSettingsAsync(guildId).ConfigureAwait(false);
        if (settings.Songs.Count == 0) return [];

        var jaroWinkler = new JaroWinkler(0.4);
        var lowerSearchTerm = searchTerm.ToLowerInvariant();

        return settings.Songs
            .Select(song =>
            {
                var titleScore = string.IsNullOrEmpty(song.Title)
                    ? 0
                    : jaroWinkler.Similarity(song.Title.ToLowerInvariant(), lowerSearchTerm);
                var uriScore = string.IsNullOrEmpty(song.Uri)
                    ? 0
                    : jaroWinkler.Similarity(song.Uri.ToLowerInvariant(), lowerSearchTerm);
                var artistScore = string.IsNullOrEmpty(song.Artist)
                    ? 0
                    : jaroWinkler.Similarity(song.Artist.ToLowerInvariant(), lowerSearchTerm);

                var combinedScore = Math.Max(Math.Max(titleScore, uriScore), artistScore);

                var matches = titleScore >= _musicConfig.TitleSimilarityCutoff ||
                              uriScore >= _musicConfig.UriSimilarityCutoff ||
                              artistScore >= _musicConfig.ArtistSimilarityCutoff;

                return (Song: song, Score: combinedScore, Matches: matches);
            })
            .Where(x => x.Matches)
            .OrderByDescending(x => x.Score)
            .Select(x => new SongHistoryEntryInfo(x.Song.Title, x.Song.Uri))
            .Distinct()
            .Take(24);
    }


    public async Task<float> GetGuildVolumeAsync(ulong guildId)
    {
        var settings = await GetOrAddSettingsAsync(guildId).ConfigureAwait(false);
        return settings.Volume;
    }

    public async Task SetGuildVolumeAsync(ulong guildId, double volume)
    {
        var clampedVolume = Math.Clamp(volume, 0.0, 2.0);

        try
        {
            var filter = Builders<GuildMusicSettingsModel>.Filter.Eq(x => x.GuildId, guildId);
            var update = Builders<GuildMusicSettingsModel>.Update
                .Set(x => x.Volume, clampedVolume)
                .SetOnInsert(x => x.GuildId, guildId)
                .SetOnInsert(x => x.Songs, [])
                .Set(x => x.UpdatedAt, DateTime.UtcNow);


            var options = new UpdateOptions { IsUpsert = true };
            await _settingsCollection.UpdateOneAsync(filter, update, options).ConfigureAwait(false);

            _logger.LogInformation("Set volume for Guild {GuildId} to {Volume}%.", guildId, clampedVolume * 100);

            var cacheKey = $"{CachePrefix}{guildId}";
            _memoryCache.Remove(cacheKey);
            await GetOrAddSettingsAsync(guildId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set volume for Guild {GuildId}.", guildId);
        }
    }
}

public record SongHistoryEntryInfo(string Title, string Uri);