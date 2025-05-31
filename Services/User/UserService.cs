using Assistant.Net.Models.User;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Assistant.Net.Services.User;

public class UserService(IMongoDatabase database, ILogger<UserService> logger)
{
    private readonly IMongoCollection<UserModel> _userCollection = database.GetCollection<UserModel>("allUsers");

    public async Task UpdateUserIntroductionAsync(ulong userId, string introduction)
    {
        try
        {
            var filter = Builders<UserModel>.Filter.Eq(u => u.UserId, userId);
            var update = Builders<UserModel>.Update
                .Set(u => u.About, introduction)
                .SetOnInsert(u => u.UserId, userId);

            var options = new UpdateOptions { IsUpsert = true };

            var result = await _userCollection.UpdateOneAsync(filter, update, options).ConfigureAwait(false);

            if (result.IsAcknowledged)
            {
                if (result is { MatchedCount: > 0, ModifiedCount: 0 } && result.UpsertedId == null)
                    logger.LogInformation("Introduction for User {UserId} submitted, but no changes detected.", userId);
                else if (result.UpsertedId != null)
                    logger.LogInformation("Successfully upserted introduction and set LastSeen for new User {UserId}.",
                        userId);
                else
                    logger.LogInformation("Successfully updated introduction and LastSeen for User {UserId}.", userId);
            }
            else
            {
                logger.LogWarning("MongoDB update for user introduction (UserId: {UserId}) was not acknowledged.",
                    userId);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating user introduction for UserId {UserId}", userId);
            throw;
        }
    }

    // This method now explicitly sets LastSeen to UtcNow when called.
    public async Task UpdateLastSeenAsync(ulong userId)
    {
        try
        {
            var filter = Builders<UserModel>.Filter.Eq(u => u.UserId, userId);
            var update = Builders<UserModel>.Update
                .Set(u => u.LastSeen, DateTime.UtcNow)
                .SetOnInsert(u => u.UserId, userId);

            var options = new UpdateOptions { IsUpsert = true };
            var result = await _userCollection.UpdateOneAsync(filter, update, options).ConfigureAwait(false);

            if (result.IsAcknowledged)
            {
                if (result.UpsertedId != null)
                    logger.LogDebug("Upserted user document for {UserId} while updating LastSeen.", userId);
                else if (result.ModifiedCount > 0)
                    logger.LogDebug("Updated LastSeen for User {UserId}.", userId);
            }
            else
            {
                logger.LogWarning("MongoDB update for LastSeen (UserId: {UserId}) was not acknowledged.", userId);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating last seen for UserId {UserId}", userId);
        }
    }

    public async Task<UserModel?> GetUserAsync(ulong userId)
    {
        try
        {
            var filter = Builders<UserModel>.Filter.Eq(u => u.UserId, userId);
            return await _userCollection.Find(filter).FirstOrDefaultAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving user data for UserId {UserId}", userId);
            return null;
        }
    }
}