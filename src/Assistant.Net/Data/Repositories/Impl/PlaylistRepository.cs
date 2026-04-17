using Assistant.Net.Data.Entities;
using Assistant.Net.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Net.Data.Repositories.Impl;

public class PlaylistRepository(AssistantDbContext context) : IPlaylistRepository
{
    public async Task<bool> ExistsAsync(ulong userId, ulong guildId, string name)
    {
        return await context.Playlists.AnyAsync(p => p.UserId == userId && p.GuildId == guildId && p.Name == name)
            .ConfigureAwait(false);
    }

    public async Task<int> GetCountAsync(ulong userId, ulong guildId)
    {
        return await context.Playlists.CountAsync(p => p.UserId == userId && p.GuildId == guildId)
            .ConfigureAwait(false);
    }

    public async Task<PlaylistEntity?> GetAsync(ulong userId, ulong guildId, string name)
    {
        return await context.Playlists
            .Include(p => p.Items)
            .ThenInclude(i => i.Track)
            .FirstOrDefaultAsync(p => p.UserId == userId && p.GuildId == guildId && p.Name == name)
            .ConfigureAwait(false);
    }

    public async Task<PlaylistEntity?> GetByIdAsync(ulong userId, ulong guildId, long playlistId)
    {
        return await context.Playlists
            .Include(p => p.Items)
            .ThenInclude(i => i.Track)
            .FirstOrDefaultAsync(p => p.UserId == userId && p.GuildId == guildId && p.Id == playlistId)
            .ConfigureAwait(false);
    }

    public async Task<List<PlaylistEntity>> GetAllAsync(ulong userId, ulong guildId)
    {
        return await context.Playlists
            .Where(p => p.UserId == userId && p.GuildId == guildId)
            .OrderBy(p => p.Name)
            .Include(p => p.Items)
            .ToListAsync()
            .ConfigureAwait(false);
    }

    public void Add(PlaylistEntity playlist)
    {
        context.Playlists.Add(playlist);
    }

    public void Remove(PlaylistEntity playlist)
    {
        context.Playlists.Remove(playlist);
    }

    public async Task<TrackEntity?> GetTrackByUriAsync(string trackUri)
    {
        return await context.Tracks.FirstOrDefaultAsync(t => t.Uri == trackUri).ConfigureAwait(false);
    }

    public void AddTrack(TrackEntity track)
    {
        context.Tracks.Add(track);
    }

    public void AddPlaylistItem(PlaylistItemEntity item)
    {
        context.PlaylistItems.Add(item);
    }

    public void RemovePlaylistItem(PlaylistItemEntity item)
    {
        context.PlaylistItems.Remove(item);
    }
}