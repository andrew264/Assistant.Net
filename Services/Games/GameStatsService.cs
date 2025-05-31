using Assistant.Net.Models.Games;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Assistant.Net.Services.Games;

public class GameStatsService
{
    // --- Constants ---
    public const double DefaultElo = 1000.0;
    private const double KFactor = 32.0;

    public const string TicTacToeGameName = "tictactoe";
    public const string RpsGameName = "rps";

    public const string HandCricketGameName = "handcricket";
    // Add other game name constants here

    private static readonly string[] KnownGames = [TicTacToeGameName, RpsGameName, HandCricketGameName];

    // --- Fields ---
    private readonly IMongoCollection<GameStatsModel> _gameStatsCollection;
    private readonly ILogger<GameStatsService> _logger;

    // --- Constructor ---
    public GameStatsService(IMongoDatabase database, ILogger<GameStatsService> logger)
    {
        _logger = logger;
        _gameStatsCollection = database.GetCollection<GameStatsModel>("gameStats");
        EnsureIndexesAsync().ConfigureAwait(false).GetAwaiter().GetResult();
    }

    // --- Index Management ---
    private async Task EnsureIndexesAsync()
    {
        var leaderboardIndexModels = (from gameName in KnownGames
            let leaderboardKeys = Builders<GameStatsModel>.IndexKeys.Ascending(g => g.Id.GuildId)
                .Descending($"games.{gameName}.elo")
            let leaderboardOptions = new CreateIndexOptions
                { Name = $"GuildId_Game_{gameName}_Elo_Desc", Sparse = true }
            select new CreateIndexModel<GameStatsModel>(leaderboardKeys, leaderboardOptions)).ToList();

        if (leaderboardIndexModels.Count == 0)
        {
            _logger.LogInformation("No specific game leaderboard indexes configured to create for gameStats.");
            return;
        }

        try
        {
            await _gameStatsCollection.Indexes.CreateManyAsync(leaderboardIndexModels).ConfigureAwait(false);
            _logger.LogInformation("Ensured leaderboard indexes on gameStats collection.");
        }
        catch (MongoCommandException ex) when (ex.CodeName is "IndexOptionsConflict" or "IndexKeySpecsConflict"
                                                   or "IndexAlreadyExists")
        {
            _logger.LogWarning(
                "One or more leaderboard indexes on gameStats already exist with potentially different options or keys: {ErrorMessage}. This might be okay if definitions match.",
                ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating leaderboard indexes on gameStats collection.");
        }
    }

    // --- Data Retrieval ---

    /// <summary>
    ///     Creates a filter definition to find a document by its compound ID.
    /// </summary>
    private static FilterDefinition<GameStatsModel> CreateIdFilter(ulong userId, ulong guildId)
    {
        var compositeId = new GameStatsIdKey { UserId = userId, GuildId = guildId };
        return Builders<GameStatsModel>.Filter.Eq(g => g.Id, compositeId);
    }

    /// <summary>
    ///     Gets the entire game statistics document for a specific user in a specific guild using the compound ID.
    ///     Returns null if the user has no stats document in that guild.
    /// </summary>
    public async Task<GameStatsModel?> GetUserGuildStatsAsync(ulong userId, ulong guildId)
    {
        var filter = CreateIdFilter(userId, guildId);
        try
        {
            return await _gameStatsCollection.Find(filter).FirstOrDefaultAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching stats document for User {UserId}, Guild {GuildId}", userId, guildId);
            return null;
        }
    }

    /// <summary>
    ///     Gets the statistics for a single game for a specific user in a specific guild.
    ///     Returns default stats if the game or user document doesn't exist.
    /// </summary>
    private async Task<SingleGameStats> GetSingleGameStatAsync(ulong userId, ulong guildId, string gameName)
    {
        var statsDoc = await GetUserGuildStatsAsync(userId, guildId).ConfigureAwait(false);
        return statsDoc?.Games.GetValueOrDefault(gameName) ?? new SingleGameStats();
    }

    /// <summary>
    ///     Fetches leaderboard data (compound ID and game-specific stats) for a specific game in a guild,
    ///     sorted by Elo descending.
    /// </summary>
    public async Task<List<GameStatsModel>> GetLeaderboardAsync(ulong guildId, string gameName, int limit)
    {
        var filter = Builders<GameStatsModel>.Filter.And(
            Builders<GameStatsModel>.Filter.Eq(g => g.Id.GuildId, guildId),
            Builders<GameStatsModel>.Filter.Exists($"games.{gameName}")
        );

        var sort = Builders<GameStatsModel>.Sort.Descending($"games.{gameName}.elo");

        var projection = Builders<GameStatsModel>.Projection
            .Include(g => g.Id)
            .Include($"games.{gameName}");

        try
        {
            return await _gameStatsCollection.Find(filter)
                .Sort(sort)
                .Limit(limit)
                .Project<GameStatsModel>(projection)
                .ToListAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching leaderboard for Game {GameName} in Guild {GuildId}", gameName,
                guildId);
            return [];
        }
    }

    // --- Data Modification ---

    /// <summary>
    ///     Ensures that the user document (identified by compound ID) and the specific game's stats subdocument exist,
    ///     creating them with default values if necessary. Returns the ensured game stats.
    /// </summary>
    private async Task<SingleGameStats> EnsurePlayerGameStatsAsync(ulong userId, ulong guildId, string gameName)
    {
        var filter = CreateIdFilter(userId, guildId);
        var gameFieldPath = $"games.{gameName}";
        var compositeId = new GameStatsIdKey { UserId = userId, GuildId = guildId };

        // 1. Upsert the main document structure if it doesn't exist.
        var upsertUserDocUpdate = Builders<GameStatsModel>.Update
            .SetOnInsert(g => g.Id, compositeId)
            .SetOnInsert(g => g.Games, new Dictionary<string, SingleGameStats>());
        await _gameStatsCollection.UpdateOneAsync(filter, upsertUserDocUpdate, new UpdateOptions { IsUpsert = true })
            .ConfigureAwait(false);

        // 2. Ensure the specific game stats subdocument exists within the 'games' dictionary.
        var gameExistsFilter = Builders<GameStatsModel>.Filter.And(
            filter,
            Builders<GameStatsModel>.Filter.Exists(gameFieldPath, false)
        );
        var setGameStatsUpdate = Builders<GameStatsModel>.Update.Set(gameFieldPath, new SingleGameStats());
        var updateResult = await _gameStatsCollection.UpdateOneAsync(gameExistsFilter, setGameStatsUpdate)
            .ConfigureAwait(false);

        switch (updateResult.IsAcknowledged)
        {
            case true when updateResult.MatchedCount > 0:
                _logger.LogDebug("Initialized {GameName} stats for User {UserId} in Guild {GuildId}", gameName, userId,
                    guildId);
                break;
            case false:
                _logger.LogWarning(
                    "DB Write not acknowledged when ensuring game stats for User {UserId}, Guild {GuildId}, Game {GameName}",
                    userId, guildId, gameName);
                break;
        }

        // 3. Fetch and return the potentially newly created or existing stats
        return await GetSingleGameStatAsync(userId, guildId, gameName).ConfigureAwait(false);
    }

    /// <summary>
    ///     Calculates and updates the Elo ratings for two players after a match.
    /// </summary>
    private async Task UpdateEloAsync(ulong player1Id, ulong player2Id, ulong guildId, string gameName,
        double player1Score) // player1Score: 1.0 for win, 0.5 for tie, 0.0 for loss
    {
        try
        {
            var player1Stats = await EnsurePlayerGameStatsAsync(player1Id, guildId, gameName).ConfigureAwait(false);
            var player2Stats = await EnsurePlayerGameStatsAsync(player2Id, guildId, gameName).ConfigureAwait(false);

            var player1Elo = player1Stats.Elo;
            var player2Elo = player2Stats.Elo;

            var expectedPlayer1 = CalculateExpectedScore(player1Elo, player2Elo);
            var expectedPlayer2 = CalculateExpectedScore(player2Elo, player1Elo);

            var player2Score = 1.0 - player1Score;

            var newPlayer1Elo = player1Elo + KFactor * (player1Score - expectedPlayer1);
            var newPlayer2Elo = player2Elo + KFactor * (player2Score - expectedPlayer2);

            // Update Player 1 Elo using the compound ID filter
            var filter1 = CreateIdFilter(player1Id, guildId);
            var update1 = Builders<GameStatsModel>.Update.Set($"games.{gameName}.elo", newPlayer1Elo);
            await _gameStatsCollection.UpdateOneAsync(filter1, update1).ConfigureAwait(false);

            // Update Player 2 Elo using the compound ID filter
            var filter2 = CreateIdFilter(player2Id, guildId);
            var update2 = Builders<GameStatsModel>.Update.Set($"games.{gameName}.elo", newPlayer2Elo);
            await _gameStatsCollection.UpdateOneAsync(filter2, update2).ConfigureAwait(false);

            _logger.LogInformation(
                "Elo Updated ({GameName}, Guild {GuildId}): P1={P1Id} ({P1OldElo:F1} -> {P1NewElo:F1}), P2={P2Id} ({P2OldElo:F1} -> {P2NewElo:F1}), P1_Score={P1Score}",
                gameName, guildId, player1Id, player1Elo, newPlayer1Elo, player2Id, player2Elo, newPlayer2Elo,
                player1Score);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating Elo for {GameName} (Guild {GuildId}, P1={Player1Id}, P2={Player2Id})",
                gameName, guildId, player1Id, player2Id);
        }
    }

    /// <summary>
    ///     Records the result of a game, updating wins, losses, ties, matches played, and Elo ratings.
    /// </summary>
    public async Task RecordGameResultAsync(ulong winnerId, ulong loserId, ulong guildId, string gameName,
        bool isTie = false)
    {
        if (winnerId == loserId && !isTie)
        {
            _logger.LogWarning(
                "Winner and loser are the same ({PlayerId}) but not a tie for {GameName} in Guild {GuildId}. Skipping stats recording.",
                winnerId, gameName, guildId);
            return;
        }

        try
        {
            // Ensure stats structures exist. No need to store the result here,
            // as the atomic updates below handle the increments.
            await EnsurePlayerGameStatsAsync(winnerId, guildId, gameName).ConfigureAwait(false);
            await EnsurePlayerGameStatsAsync(loserId, guildId, gameName).ConfigureAwait(false);

            // Use compound ID filters for updates
            var winnerFilter = CreateIdFilter(winnerId, guildId);
            var loserFilter = CreateIdFilter(loserId, guildId);
            var gamePath = $"games.{gameName}";

            if (isTie)
            {
                var tieUpdate = Builders<GameStatsModel>.Update
                    .Inc($"{gamePath}.ties", 1)
                    .Inc($"{gamePath}.matches_played", 1);
                await _gameStatsCollection.UpdateOneAsync(winnerFilter, tieUpdate).ConfigureAwait(false);
                await _gameStatsCollection.UpdateOneAsync(loserFilter, tieUpdate).ConfigureAwait(false);
                _logger.LogInformation("Recorded Tie ({GameName}, Guild {GuildId}): P1={Player1Id}, P2={Player2Id}",
                    gameName, guildId, winnerId, loserId);
                await UpdateEloAsync(winnerId, loserId, guildId, gameName, 0.5).ConfigureAwait(false);
            }
            else
            {
                var winnerUpdate = Builders<GameStatsModel>.Update
                    .Inc($"{gamePath}.wins", 1)
                    .Inc($"{gamePath}.matches_played", 1);
                await _gameStatsCollection.UpdateOneAsync(winnerFilter, winnerUpdate).ConfigureAwait(false);

                var loserUpdate = Builders<GameStatsModel>.Update
                    .Inc($"{gamePath}.losses", 1)
                    .Inc($"{gamePath}.matches_played", 1);
                await _gameStatsCollection.UpdateOneAsync(loserFilter, loserUpdate).ConfigureAwait(false);
                _logger.LogInformation(
                    "Recorded Win/Loss ({GameName}, Guild {GuildId}): Winner={WinnerId}, Loser={LoserId}", gameName,
                    guildId, winnerId, loserId);
                await UpdateEloAsync(winnerId, loserId, guildId, gameName, 1.0).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to record game result for {GameName} (Guild {GuildId}, Winner={WinnerId}, Loser={LoserId}, Tie={IsTie})",
                gameName, guildId, winnerId, loserId, isTie);
        }
    }

    // --- Helper Methods ---

    /// <summary>
    ///     Calculates the expected score of player A against player B based on their Elo ratings.
    /// </summary>
    private static double CalculateExpectedScore(double ratingA, double ratingB) =>
        1.0 / (1.0 + Math.Pow(10.0, (ratingB - ratingA) / 400.0));
}