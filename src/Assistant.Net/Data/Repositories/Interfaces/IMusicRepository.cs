using Assistant.Net.Data.Entities;

namespace Assistant.Net.Data.Repositories.Interfaces;

public interface IMusicRepository
{
    Task<GuildMusicSettingsEntity?> GetGuildSettingsAsync(ulong guildId);
    void AddGuildSettings(GuildMusicSettingsEntity settings);

    Task<TrackEntity?> GetTrackByUriAsync(string trackUri);
    void AddTrack(TrackEntity track);

    void AddPlayHistory(PlayHistoryEntity playHistory);
    Task<List<(TrackEntity Track, int Count)>> GetTopPlaysAsync(ulong guildId, ulong? requesterId, int limit);
    Task<List<TrackEntity>> SearchTracksAsync(string searchTerm, double similarityCutoff, int limit);
}