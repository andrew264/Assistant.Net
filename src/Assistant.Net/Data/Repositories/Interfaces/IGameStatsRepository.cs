using Assistant.Net.Data.Entities;

namespace Assistant.Net.Data.Repositories.Interfaces;

public interface IGameStatsRepository
{
    Task<List<GameStatEntity>> GetUserGuildStatsAsync(ulong userId, ulong guildId);
    Task<List<GameStatEntity>> GetLeaderboardAsync(ulong guildId, int gameType, int limit);
    Task<GameStatEntity> EnsureExistsAsync(ulong userId, ulong guildId, int gameType);
}