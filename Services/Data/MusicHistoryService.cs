using Assistant.Net.Configuration;
using Assistant.Net.Data;
using Assistant.Net.Data.Entities;
using Assistant.Net.Models.Music;
using Lavalink4NET.Players;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Services.Data;

public class MusicHistoryService
{
    private readonly IDbContextFactory<AssistantDbContext> _dbFactory;
    private readonly GuildService _guildService;
    private readonly ILogger<MusicHistoryService> _logger;
    private readonly MusicConfig _musicConfig;
    private readonly UserService _userService;

    public MusicHistoryService(
        IDbContextFactory<AssistantDbContext> dbFactory,
        ILogger<MusicHistoryService> logger,
        Config config,
        UserService userService, GuildService guildService)
    {
        _dbFactory = dbFactory;
        _logger = logger;
        _musicConfig = config.Music;
        _userService = userService;
        _guildService = guildService;

        _logger.LogInformation("MusicHistoryService initialized.");
    }

    private async Task<GuildMusicSettingsEntity> GetSettingsAsync(ulong guildId)
    {
        await using var context = await _dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        var decimalGuildId = (decimal)guildId;

        var settings = await context.GuildMusicSettings.FindAsync(decimalGuildId).ConfigureAwait(false);

        if (settings != null) return settings;
        await _guildService.EnsureGuildExistsAsync(context, guildId).ConfigureAwait(false);
        settings = new GuildMusicSettingsEntity
        {
            GuildId = decimalGuildId,
            Volume = _musicConfig.DefaultVolume
        };
        context.GuildMusicSettings.Add(settings);
        await context.SaveChangesAsync().ConfigureAwait(false);

        return settings;
    }

    public async Task AddSongToHistoryAsync(ulong guildId, ITrackQueueItem queueItem)
    {
        if (queueItem.Track?.Uri is null)
        {
            _logger.LogWarning("Cannot add track to history: Track or URI is null. Guild: {GuildId}", guildId);
            return;
        }

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
            var settings = await context.GuildMusicSettings.FindAsync(decimalGuildId).ConfigureAwait(false);
            if (settings == null)
            {
                await _guildService.EnsureGuildExistsAsync(context, guildId).ConfigureAwait(false);
                settings = new GuildMusicSettingsEntity
                {
                    GuildId = decimalGuildId,
                    Volume = _musicConfig.DefaultVolume
                };
                context.GuildMusicSettings.Add(settings);
            }

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
                GuildId = settings.GuildId,
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

    public async Task<IEnumerable<SongHistoryEntryInfo>> SearchSongHistoryAsync(ulong guildId, string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm)) return [];

        await using var context = await _dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        var decimalGuildId = (decimal)guildId;
        var cutoff = _musicConfig.TitleSimilarityCutoff;

        var tracks = await context.PlayHistories
            .Where(ph => ph.GuildId == decimalGuildId)
            .Include(ph => ph.Track)
            .Select(ph => ph.Track)
            .Where(t =>
                EF.Functions.TrigramsSimilarity(t.Title, searchTerm) > cutoff ||
                EF.Functions.TrigramsSimilarity(t.Uri, searchTerm) > cutoff ||
                (t.Artist != null && EF.Functions.TrigramsSimilarity(t.Artist, searchTerm) > cutoff))
            .OrderByDescending(t =>
                EF.Functions.TrigramsSimilarity(t.Title, searchTerm) +
                EF.Functions.TrigramsSimilarity(t.Uri, searchTerm) +
                (t.Artist == null ? 0 : EF.Functions.TrigramsSimilarity(t.Artist, searchTerm))
            )
            .Distinct()
            .Take(24)
            .ToListAsync()
            .ConfigureAwait(false);

        return tracks.Select(t => new SongHistoryEntryInfo(t.Title, t.Uri));
    }

    public async Task<float> GetGuildVolumeAsync(ulong guildId)
    {
        var settings = await GetSettingsAsync(guildId).ConfigureAwait(false);
        return settings.Volume;
    }

    public async Task SetGuildVolumeAsync(ulong guildId, double volume)
    {
        var clampedVolume = (float)Math.Clamp(volume, 0.0, 2.0);

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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set volume for Guild {GuildId}.", guildId);
        }
    }
}

public record SongHistoryEntryInfo(string Title, string Uri);