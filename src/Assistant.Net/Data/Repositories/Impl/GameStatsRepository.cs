using Assistant.Net.Data.Entities;
using Assistant.Net.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Net.Data.Repositories.Impl;

public class GameStatsRepository(AssistantDbContext context) : IGameStatsRepository
{
    public async Task<List<GameStatEntity>> GetUserGuildStatsAsync(ulong userId, ulong guildId)
    {
        var dUserId = (decimal)userId;
        var dGuildId = (decimal)guildId;

        return await context.GameStats
            .Where(g => g.UserId == dUserId && g.GuildId == dGuildId)
            .ToListAsync()
            .ConfigureAwait(false);
    }

    public async Task<List<GameStatEntity>> GetLeaderboardAsync(ulong guildId, int gameType, int limit)
    {
        var dGuildId = (decimal)guildId;

        return await context.GameStats
            .Where(g => g.GuildId == dGuildId && g.GameType == gameType)
            .OrderByDescending(g => g.Elo)
            .Take(limit)
            .ToListAsync()
            .ConfigureAwait(false);
    }

    public async Task<GameStatEntity> EnsureExistsAsync(ulong userId, ulong guildId, int gameType)
    {
        var dUserId = (decimal)userId;
        var dGuildId = (decimal)guildId;

        var stat = await context.GameStats
            .FirstOrDefaultAsync(g => g.UserId == dUserId && g.GuildId == dGuildId && g.GameType == gameType)
            .ConfigureAwait(false);

        if (stat != null) return stat;

        stat = new GameStatEntity
        {
            UserId = dUserId,
            GuildId = dGuildId,
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