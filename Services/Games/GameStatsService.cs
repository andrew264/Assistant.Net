using Assistant.Net.Data;
using Assistant.Net.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Services.Games;

public class GameStatsService(IDbContextFactory<AssistantDbContext> dbFactory, ILogger<GameStatsService> logger)
{
    public const double DefaultElo = 1000.0;
    private const double KFactor = 32.0;

    public const string TicTacToeGameName = "tictactoe";
    public const string RpsGameName = "rps";
    public const string HandCricketGameName = "handcricket";

    public static int GetGameType(string gameName) => gameName.ToLowerInvariant() switch
    {
        TicTacToeGameName => 0,
        RpsGameName => 1,
        HandCricketGameName => 2,
        _ => -1
    };

    public static string GetGameName(int gameType) => gameType switch
    {
        0 => TicTacToeGameName,
        1 => RpsGameName,
        2 => HandCricketGameName,
        _ => "unknown"
    };

    public async Task<List<GameStatEntity>> GetUserGuildStatsAsync(ulong userId, ulong guildId)
    {
        await using var context = await dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        var decimalUserId = (decimal)userId;
        var decimalGuildId = (decimal)guildId;

        return await context.GameStats
            .Where(g => g.UserId == decimalUserId && g.GuildId == decimalGuildId)
            .ToListAsync()
            .ConfigureAwait(false);
    }

    public async Task<List<GameStatEntity>> GetLeaderboardAsync(ulong guildId, string gameName, int limit)
    {
        var gameType = GetGameType(gameName);
        if (gameType == -1) return [];

        await using var context = await dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        var decimalGuildId = (decimal)guildId;

        return await context.GameStats
            .Where(g => g.GuildId == decimalGuildId && g.GameType == gameType)
            .OrderByDescending(g => g.Elo)
            .Take(limit)
            .ToListAsync()
            .ConfigureAwait(false);
    }

    private async Task<GameStatEntity> EnsureGameStatAsync(AssistantDbContext context, decimal userId, decimal guildId,
        int gameType)
    {
        var stat = await context.GameStats
            .FirstOrDefaultAsync(g => g.UserId == userId && g.GuildId == guildId && g.GameType == gameType)
            .ConfigureAwait(false);

        if (stat != null) return stat;

        if (!await context.Users.AnyAsync(u => u.Id == userId).ConfigureAwait(false))
            context.Users.Add(new UserEntity { Id = userId });

        stat = new GameStatEntity
        {
            UserId = userId,
            GuildId = guildId,
            GameType = gameType,
            Elo = DefaultElo,
            Wins = 0,
            Losses = 0,
            Ties = 0
        };
        context.GameStats.Add(stat);

        return stat;
    }

    private static double CalculateExpectedScore(double ratingA, double ratingB) =>
        1.0 / (1.0 + Math.Pow(10.0, (ratingB - ratingA) / 400.0));

    public async Task RecordGameResultAsync(ulong winnerId, ulong loserId, ulong guildId, string gameName,
        bool isTie = false)
    {
        if (winnerId == loserId && !isTie)
        {
            logger.LogWarning(
                "Winner and loser are the same ({PlayerId}) but not a tie for {GameName} in Guild {GuildId}. Skipping stats recording.",
                winnerId, gameName, guildId);
            return;
        }

        var gameType = GetGameType(gameName);
        if (gameType == -1)
        {
            logger.LogError("Unknown game type: {GameName}", gameName);
            return;
        }

        await using var context = await dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        var dWinnerId = (decimal)winnerId;
        var dLoserId = (decimal)loserId;
        var dGuildId = (decimal)guildId;

        try
        {
            var winnerStats =
                await EnsureGameStatAsync(context, dWinnerId, dGuildId, gameType).ConfigureAwait(false);
            var loserStats = await EnsureGameStatAsync(context, dLoserId, dGuildId, gameType).ConfigureAwait(false);

            var player1Score = isTie ? 0.5 : 1.0;
            var player2Score = 1.0 - player1Score;

            var expectedP1 = CalculateExpectedScore(winnerStats.Elo, loserStats.Elo);
            var expectedP2 = CalculateExpectedScore(loserStats.Elo, winnerStats.Elo);

            winnerStats.Elo += KFactor * (player1Score - expectedP1);
            loserStats.Elo += KFactor * (player2Score - expectedP2);

            if (isTie)
            {
                winnerStats.Ties++;
                loserStats.Ties++;
            }
            else
            {
                winnerStats.Wins++;
                loserStats.Losses++;
            }

            await context.SaveChangesAsync().ConfigureAwait(false);
            logger.LogInformation(
                "Recorded {Result} for {GameName} in {GuildId}. P1: {P1}, P2: {P2}",
                isTie ? "Tie" : "Win", gameName, guildId, winnerId, loserId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to record game result for {GameName} (Guild {GuildId}, Winner={WinnerId}, Loser={LoserId})",
                gameName, guildId, winnerId, loserId);
        }
    }
}