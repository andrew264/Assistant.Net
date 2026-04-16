using Assistant.Net.Data.Entities;

namespace Assistant.Net.Data.Repositories.Interfaces;

public interface IPlaylistRepository
{
    Task<bool> ExistsAsync(ulong userId, ulong guildId, string name);
    Task<int> GetCountAsync(ulong userId, ulong guildId);

    Task<PlaylistEntity?> GetAsync(ulong userId, ulong guildId, string name);
    Task<PlaylistEntity?> GetByIdAsync(ulong userId, ulong guildId, long playlistId);
    Task<List<PlaylistEntity>> GetAllAsync(ulong userId, ulong guildId);

    void Add(PlaylistEntity playlist);
    void Remove(PlaylistEntity playlist);

    Task<TrackEntity?> GetTrackByUriAsync(string trackUri);
    void AddTrack(TrackEntity track);

    void AddPlaylistItem(PlaylistItemEntity item);
    void RemovePlaylistItem(PlaylistItemEntity item);
}