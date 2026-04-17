using Assistant.Net.Data.Entities;
using Assistant.Net.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Net.Data.Repositories.Impl;

public class GameStatsRepository(AssistantDbContext context) : IGameStatsRepository
{
    public async Task<List<GameStatEntity>> GetUserGuildStatsAsync(ulong userId, ulong guildId)
    {
        return await context.GameStats
            .AsNoTracking()
            .Where(g => g.UserId == userId && g.GuildId == guildId)
            .ToListAsync()
            .ConfigureAwait(false);
    }

    public async Task<Dictionary<ulong, GameStatEntity>> GetOrCreateStatsAsync(ulong guildId, int gameType,
        IEnumerable<ulong> userIds)
    {
        var distinctIds = userIds.Distinct().ToList();

        var existingStats = await context.GameStats
            .Where(g => g.GuildId == guildId && g.GameType == gameType && distinctIds.Contains(g.UserId))
            .ToListAsync()
            .ConfigureAwait(false);

        var statsDict = existingStats.ToDictionary(s => s.UserId);

        foreach (var id in distinctIds)
        {
            if (statsDict.ContainsKey(id)) continue;
            var newStat = new GameStatEntity
            {
                UserId = id,
                GuildId = guildId,
                GameType = gameType,
                Elo = 1000.0,
                Wins = 0,
                Losses = 0,
                Ties = 0
            };

            context.GameStats.Add(newStat);
            statsDict[id] = newStat;
        }

        return statsDict;
    }

    public async Task<List<GameStatEntity>> GetLeaderboardAsync(ulong guildId, int gameType, int limit)
    {
        return await context.GameStats
            .AsNoTracking()
            .Where(g => g.GuildId == guildId && g.GameType == gameType)
            .OrderByDescending(g => g.Elo)
            .Take(limit)
            .ToListAsync()
            .ConfigureAwait(false);
    }
}