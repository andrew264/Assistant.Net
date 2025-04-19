using Assistant.Net.Models.Games;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Assistant.Net.Services;

public class GameStatsService
{
    public const double DefaultElo = 1000.0;
    private const double KFactor = 32.0;

    public const string TicTacToeGameName = "tictactoe";
    // TODO: Add other game names here if needed: public const string RpsGameName = "rps";

    private readonly IMongoCollection<GameStatsModel> _gameStatsCollection;
    private readonly ILogger<GameStatsService> _logger;

    public GameStatsService(IMongoDatabase database, ILogger<GameStatsService> logger)
    {
        _logger = logger;
        _gameStatsCollection = database.GetCollection<GameStatsModel>("game_stats");

        // Create indexes for faster lookups if they don't exist
        var indexKeys = Builders<GameStatsModel>.IndexKeys.Ascending(g => g.UserId).Ascending(g => g.GuildId);
        var indexOptions = new CreateIndexOptions { Unique = true, Name = "UserId_GuildId_Unique" };
        var indexModel = new CreateIndexModel<GameStatsModel>(indexKeys, indexOptions);
        try
        {
            _gameStatsCollection.Indexes.CreateOne(indexModel);
            _logger.LogInformation("Ensured unique index on game_stats (UserId, GuildId)");
        }
        catch (MongoCommandException ex) when (ex.CodeName == "IndexOptionsConflict" ||
                                               ex.CodeName == "IndexKeySpecsConflict")
        {
            _logger.LogWarning("Index on game_stats (UserId, GuildId) already exists with different options or keys.");
            // Potentially drop and recreate if necessary, but warning is safer for now.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating index on game_stats collection.");
        }
    }

    private static FilterDefinition<GameStatsModel> GetUserGuildFilter(ulong userId, ulong guildId)
    {
        return Builders<GameStatsModel>.Filter.And(
            Builders<GameStatsModel>.Filter.Eq(g => g.UserId, userId),
            Builders<GameStatsModel>.Filter.Eq(g => g.GuildId, guildId)
        );
    }

    public async Task<SingleGameStats?> GetPlayerGameStatsAsync(ulong userId, ulong guildId, string gameName)
    {
        var filter = GetUserGuildFilter(userId, guildId);
        var projection = Builders<GameStatsModel>.Projection
            .Include(g => g.Games)
            .Exclude(g => g.UserId) // Don't need these in the result
            .Exclude(g => g.GuildId)
            .Exclude("_id"); // Exclude MongoDB's default _id

        var statsDoc = await _gameStatsCollection.Find(filter).Project<GameStatsModel>(projection)
            .FirstOrDefaultAsync();

        return statsDoc?.Games?.GetValueOrDefault(gameName);
    }

    private async Task<SingleGameStats> EnsurePlayerGameStatsAsync(ulong userId, ulong guildId, string gameName)
    {
        var filter = GetUserGuildFilter(userId, guildId);
        var gameField = $"games.{gameName}";

        // 1. Ensure the top-level document exists
        var updateDoc = Builders<GameStatsModel>.Update
            .SetOnInsert(g => g.UserId, userId)
            .SetOnInsert(g => g.GuildId, guildId)
            .SetOnInsert(g => g.Games,
                new Dictionary<string, SingleGameStats>()); // Initialize games dict if doc is new
        await _gameStatsCollection.UpdateOneAsync(filter, updateDoc, new UpdateOptions { IsUpsert = true });

        // 2. Ensure the specific game stats exist within the 'games' dictionary
        var gameExistsFilter = Builders<GameStatsModel>.Filter.And(
            filter,
            Builders<GameStatsModel>.Filter.Exists(gameField, false) // Check if the game field does *not* exist
        );

        var updateGame =
            Builders<GameStatsModel>.Update.Set(gameField, new SingleGameStats()); // Set default game stats

        var updateResult = await _gameStatsCollection.UpdateOneAsync(gameExistsFilter, updateGame);

        if (updateResult.IsAcknowledged && updateResult.ModifiedCount > 0)
            _logger.LogDebug("Initialized {GameName} stats for User {UserId} in Guild {GuildId}", gameName, userId,
                guildId);
        else if (!updateResult.IsAcknowledged)
            _logger.LogWarning(
                "Failed to get acknowledgment when initializing game stats for User {UserId}, Guild {GuildId}, Game {GameName}",
                userId, guildId, gameName);

        // 3. Fetch and return the (potentially) newly created stats
        var stats = await GetPlayerGameStatsAsync(userId, guildId, gameName);
        return stats ?? new SingleGameStats(); // Should not be null if upsert worked, but return default just in case
    }

    private static double CalculateExpectedScore(double ratingA, double ratingB)
    {
        return 1.0 / (1.0 + Math.Pow(10.0, (ratingB - ratingA) / 400.0));
    }

    private async Task UpdateEloAsync(ulong player1Id, ulong player2Id, ulong guildId, string gameName, bool isTie)
    {
        try
        {
            var player1Stats = await EnsurePlayerGameStatsAsync(player1Id, guildId, gameName);
            var player2Stats = await EnsurePlayerGameStatsAsync(player2Id, guildId, gameName);

            var player1Elo = player1Stats.Elo;
            var player2Elo = player2Stats.Elo;

            var expectedPlayer1 = CalculateExpectedScore(player1Elo, player2Elo);
            var expectedPlayer2 = CalculateExpectedScore(player2Elo, player1Elo);

            var actualPlayer1 = isTie ? 0.5 : 1.0; // If P1 won, score is 1, if tie, 0.5
            var actualPlayer2 = isTie ? 0.5 : 0.0; // If P1 won, P2 score is 0, if tie, 0.5

            var newPlayer1Elo = player1Elo + KFactor * (actualPlayer1 - expectedPlayer1);
            var newPlayer2Elo = player2Elo + KFactor * (actualPlayer2 - expectedPlayer2);

            var filter1 = GetUserGuildFilter(player1Id, guildId);
            var update1 = Builders<GameStatsModel>.Update.Set($"games.{gameName}.elo", newPlayer1Elo);
            await _gameStatsCollection.UpdateOneAsync(filter1, update1);

            var filter2 = GetUserGuildFilter(player2Id, guildId);
            var update2 = Builders<GameStatsModel>.Update.Set($"games.{gameName}.elo", newPlayer2Elo);
            await _gameStatsCollection.UpdateOneAsync(filter2, update2);

            _logger.LogInformation(
                "Elo Updated ({GameName}, Guild {GuildId}): P1={P1Id} ({P1OldElo:F1} -> {P1NewElo:F1}), P2={P2Id} ({P2OldElo:F1} -> {P2NewElo:F1}), Tie={IsTie}",
                gameName, guildId, player1Id, player1Elo, newPlayer1Elo, player2Id, player2Elo, newPlayer2Elo, isTie);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating Elo for {GameName} (Guild {GuildId}, {Player1Id} vs {Player2Id})",
                gameName, guildId, player1Id, player2Id);
        }
    }

    public async Task RecordGameResultAsync(ulong winnerId, ulong loserId, ulong guildId, string gameName,
        bool isTie = false)
    {
        // Basic validation
        if (winnerId == loserId && !isTie)
        {
            _logger.LogWarning(
                "Winner and loser are the same ({PlayerId}) but not a tie for {GameName} in Guild {GuildId}. Skipping stats recording.",
                winnerId, gameName, guildId);
            return;
        }
        // Add checks for valid gameName if necessary

        try
        {
            // Ensure stats structures exist before incrementing
            await EnsurePlayerGameStatsAsync(winnerId, guildId, gameName);
            await EnsurePlayerGameStatsAsync(loserId, guildId, gameName);

            var winnerFilter = GetUserGuildFilter(winnerId, guildId);
            var loserFilter = GetUserGuildFilter(loserId, guildId);

            // Update W/L/T and matches played
            if (isTie)
            {
                var tieUpdate = Builders<GameStatsModel>.Update
                    .Inc($"games.{gameName}.ties", 1)
                    .Inc($"games.{gameName}.matches_played", 1);
                await _gameStatsCollection.UpdateOneAsync(winnerFilter, tieUpdate);
                await _gameStatsCollection.UpdateOneAsync(loserFilter,
                    tieUpdate); // Both players get a tie and match played
                _logger.LogInformation("Recorded Tie ({GameName}, Guild {GuildId}): {Player1Id} vs {Player2Id}",
                    gameName, guildId, winnerId, loserId);
            }
            else
            {
                var winnerUpdate = Builders<GameStatsModel>.Update
                    .Inc($"games.{gameName}.wins", 1)
                    .Inc($"games.{gameName}.matches_played", 1);
                await _gameStatsCollection.UpdateOneAsync(winnerFilter, winnerUpdate);

                var loserUpdate = Builders<GameStatsModel>.Update
                    .Inc($"games.{gameName}.losses", 1)
                    .Inc($"games.{gameName}.matches_played", 1);
                await _gameStatsCollection.UpdateOneAsync(loserFilter, loserUpdate);
                _logger.LogInformation(
                    "Recorded Win/Loss ({GameName}, Guild {GuildId}): Winner={WinnerId}, Loser={LoserId}", gameName,
                    guildId, winnerId, loserId);
            }

            await UpdateEloAsync(winnerId, loserId, guildId, gameName, isTie);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to record game result for {GameName} (Guild {GuildId}, Winner {WinnerId}, Loser {LoserId}, Tie {IsTie})",
                gameName, guildId, winnerId, loserId, isTie);
            // Depending on requirements, you might want to inform the users.
        }
    }
}