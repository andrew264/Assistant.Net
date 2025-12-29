using Assistant.Net.Configuration;
using Assistant.Net.Data;
using Assistant.Net.Data.Entities;
using Assistant.Net.Models.Music;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Services.Music;

public class MusicHistoryService
{
    private readonly IDbContextFactory<AssistantDbContext> _dbFactory;
    private readonly ILogger<MusicHistoryService> _logger;
    private readonly MusicConfig _musicConfig;

    public MusicHistoryService(
        IDbContextFactory<AssistantDbContext> dbFactory,
        ILogger<MusicHistoryService> logger,
        Config config)
    {
        _dbFactory = dbFactory;
        _logger = logger;
        _musicConfig = config.Music;

        _logger.LogInformation("MusicHistoryService initialized.");
    }

    private async Task<GuildMusicSettingsEntity> GetSettingsAsync(ulong guildId)
    {
        await using var context = await _dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        var decimalGuildId = (decimal)guildId;

        var settings = await context.GuildMusicSettings.FindAsync(decimalGuildId).ConfigureAwait(false);

        if (settings != null) return settings;

        settings = new GuildMusicSettingsEntity
        {
            GuildId = decimalGuildId,
            Volume = _musicConfig.DefaultVolume
        };
        context.GuildMusicSettings.Add(settings);
        await context.SaveChangesAsync().ConfigureAwait(false);

        return settings;
    }

    public async Task AddSongToHistoryAsync(ulong guildId, SongHistoryEntry songEntry)
    {
        await using var context = await _dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        var decimalGuildId = (decimal)guildId;
        var decimalRequesterId = (decimal)songEntry.RequestedBy;

        try
        {
            var settings = await context.GuildMusicSettings.FindAsync(decimalGuildId).ConfigureAwait(false);
            if (settings == null)
            {
                settings = new GuildMusicSettingsEntity
                {
                    GuildId = decimalGuildId,
                    Volume = _musicConfig.DefaultVolume
                };
                context.GuildMusicSettings.Add(settings);
            }

            if (!await context.Users.AnyAsync(u => u.Id == decimalRequesterId).ConfigureAwait(false))
                context.Users.Add(new UserEntity { Id = decimalRequesterId });

            var track = await context.Tracks.FirstOrDefaultAsync(t => t.Uri == songEntry.Uri).ConfigureAwait(false);
            if (track == null)
            {
                track = new TrackEntity
                {
                    Uri = songEntry.Uri,
                    Title = songEntry.Title,
                    Artist = songEntry.Artist,
                    ThumbnailUrl = songEntry.ThumbnailUrl,
                    Duration = songEntry.Duration.TotalSeconds,
                    Source = "unknown"
                };
                context.Tracks.Add(track);
            }

            var playHistory = new PlayHistoryEntity
            {
                GuildSettings = settings,
                Track = track,
                RequestedBy = decimalRequesterId,
                PlayedAt = DateTime.UtcNow
            };
            context.PlayHistories.Add(playHistory);

            await context.SaveChangesAsync().ConfigureAwait(false);
            _logger.LogDebug("Added song '{SongTitle}' to history for Guild {GuildId}.", songEntry.Title, guildId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add song to history for Guild {GuildId}.", guildId);
        }
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