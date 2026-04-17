using Assistant.Net.Data.Entities;
using Assistant.Net.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Net.Data.Repositories.Impl;

public class GameStatsRepository(AssistantDbContext context) : IGameStatsRepository
{
    public async Task<List<GameStatEntity>> GetUserGuildStatsAsync(ulong userId, ulong guildId)
    {
        return await context.GameStats
            .Where(g => g.UserId == userId && g.GuildId == guildId)
            .ToListAsync()
            .ConfigureAwait(false);
    }

    public async Task<List<GameStatEntity>> GetLeaderboardAsync(ulong guildId, int gameType, int limit)
    {
        return await context.GameStats
            .Where(g => g.GuildId == guildId && g.GameType == gameType)
            .OrderByDescending(g => g.Elo)
            .Take(limit)
            .ToListAsync()
            .ConfigureAwait(false);
    }

    public async Task<GameStatEntity> EnsureExistsAsync(ulong userId, ulong guildId, int gameType)
    {
        var stat = await context.GameStats
            .FirstOrDefaultAsync(g => g.UserId == userId && g.GuildId == guildId && g.GameType == gameType)
            .ConfigureAwait(false);

        if (stat != null) return stat;

        stat = new GameStatEntity
        {
            UserId = userId,
            GuildId = guildId,
            GameType = gameType,
            Elo = 1000.0,
            Wins = 0,
            Losses = 0,
            Ties = 0
        };
        context.GameStats.Add(stat);

        return stat;
    }
}