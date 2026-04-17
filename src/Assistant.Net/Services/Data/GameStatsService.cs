using Assistant.Net.Data.Entities;
using Assistant.Net.Data.Repositories.Interfaces;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Services.Data;

public class GameStatsService(
    IUnitOfWorkFactory uowFactory,
    ILogger<GameStatsService> logger)
{
    private const double KFactor = 32.0;

    public const string TicTacToeGameName = "tictactoe";
    public const string RpsGameName = "rps";
    public const string HandCricketGameName = "handcricket";

    public static readonly IReadOnlyList<string> GameNames = new List<string>
    {
        TicTacToeGameName,
        RpsGameName,
        HandCricketGameName
    }.AsReadOnly();

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
        await using var uow = await uowFactory.CreateAsync().ConfigureAwait(false);
        return await uow.GameStats.GetUserGuildStatsAsync(userId, guildId).ConfigureAwait(false);
    }

    public async Task<List<GameStatEntity>> GetLeaderboardAsync(ulong guildId, string gameName, int limit)
    {
        var gameType = GetGameType(gameName);
        if (gameType == -1) return [];

        await using var uow = await uowFactory.CreateAsync().ConfigureAwait(false);
        return await uow.GameStats.GetLeaderboardAsync(guildId, gameType, limit).ConfigureAwait(false);
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

        await using var uow = await uowFactory.CreateAsync().ConfigureAwait(false);

        try
        {
            await uow.Users.EnsureUsersExistAsync([winnerId, loserId]).ConfigureAwait(false);
            await uow.Guilds.EnsureExistsAsync(guildId).ConfigureAwait(false);

            var stats = await uow.GameStats.GetOrCreateStatsAsync(guildId, gameType, [winnerId, loserId])
                .ConfigureAwait(false);

            var winnerStats = stats[winnerId];
            var loserStats = stats[loserId];

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

            await uow.SaveChangesAsync().ConfigureAwait(false);
            logger.LogInformation("Recorded {Result} for {GameName} in {GuildId}. P1: {P1}, P2: {P2}",
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