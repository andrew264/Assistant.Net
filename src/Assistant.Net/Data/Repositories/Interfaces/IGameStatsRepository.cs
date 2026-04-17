using Assistant.Net.Data.Entities;

namespace Assistant.Net.Data.Repositories.Interfaces;

public interface IGameStatsRepository
{
    Task<List<GameStatEntity>> GetUserGuildStatsAsync(ulong userId, ulong guildId);

    Task<Dictionary<ulong, GameStatEntity>> GetOrCreateStatsAsync(ulong guildId, int gameType,
        IEnumerable<ulong> userIds);

    Task<List<GameStatEntity>> GetLeaderboardAsync(ulong guildId, int gameType, int limit);
}