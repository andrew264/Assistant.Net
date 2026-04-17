using Assistant.Net.Data.Entities;
using Assistant.Net.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Net.Data.Repositories.Impl;

public class MusicRepository(AssistantDbContext context) : IMusicRepository
{
    public async Task<GuildMusicSettingsEntity?> GetGuildSettingsAsync(ulong guildId) =>
        await context.GuildMusicSettings.FindAsync(guildId).ConfigureAwait(false);

    public void AddGuildSettings(GuildMusicSettingsEntity settings)
    {
        context.GuildMusicSettings.Add(settings);
    }

    public async Task<TrackEntity?> GetTrackByUriAsync(string trackUri)
    {
        return await context.Tracks.FirstOrDefaultAsync(t => t.Uri == trackUri).ConfigureAwait(false);
    }

    public void AddTrack(TrackEntity track)
    {
        context.Tracks.Add(track);
    }

    public void AddPlayHistory(PlayHistoryEntity playHistory)
    {
        context.PlayHistories.Add(playHistory);
    }

    public async Task<List<(TrackEntity Track, int Count)>> GetTopPlaysAsync(ulong guildId, ulong? requesterId,
        int limit)
    {
        var query = context.PlayHistories
            .Include(ph => ph.Track)
            .Where(ph => ph.GuildId == guildId);

        if (requesterId.HasValue)
        {
            var dRequesterId = requesterId.Value;
            query = query.Where(ph => ph.RequestedBy == dRequesterId);
        }

        var result = await query
            .GroupBy(ph => ph.Track)
            .Select(g => new { Track = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(limit)
            .ToListAsync()
            .ConfigureAwait(false);

        return result.Select(x => (x.Track, x.Count)).ToList();
    }

    public async Task<List<TrackEntity>> SearchTracksAsync(string searchTerm, double similarityCutoff, int limit)
    {
        if (searchTerm.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
            searchTerm.StartsWith("www", StringComparison.OrdinalIgnoreCase))
            return await context.Tracks
                .Where(t => EF.Functions.ILike(t.Uri, $"%{searchTerm}%"))
                .OrderBy(t => t.Title)
                .Take(limit)
                .ToListAsync()
                .ConfigureAwait(false);

        return await context.Tracks
            .Where(t => EF.Functions.TrigramsSimilarity(t.Title, searchTerm) > similarityCutoff ||
                        EF.Functions.ILike(t.Title, $"%{searchTerm}%"))
            .OrderByDescending(t => t.Title)
            .Take(limit)
            .ToListAsync()
            .ConfigureAwait(false);
    }
}