using Assistant.Net.Data;
using Assistant.Net.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Services.User;

public class UserService(IDbContextFactory<AssistantDbContext> dbFactory, ILogger<UserService> logger)
{
    public async Task UpdateUserIntroductionAsync(ulong userId, string introduction)
    {
        await using var context = await dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        var decimalUserId = (decimal)userId;

        try
        {
            var user = await context.Users.FindAsync(decimalUserId).ConfigureAwait(false);
            if (user == null)
            {
                user = new UserEntity { Id = decimalUserId };
                context.Users.Add(user);
            }

            if (user.About == introduction)
            {
                logger.LogInformation("Introduction for User {UserId} submitted, but no changes detected.", userId);
                return;
            }

            user.About = introduction;
            user.LastSeen = DateTime.UtcNow;

            await context.SaveChangesAsync().ConfigureAwait(false);
            logger.LogInformation("Successfully updated introduction and LastSeen for User {UserId}.", userId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating user introduction for UserId {UserId}", userId);
            throw;
        }
    }

    public async Task UpdateLastSeenAsync(ulong userId)
    {
        await using var context = await dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        var decimalUserId = (decimal)userId;

        try
        {
            var user = await context.Users.FindAsync(decimalUserId).ConfigureAwait(false);
            if (user == null)
            {
                user = new UserEntity { Id = decimalUserId };
                context.Users.Add(user);
                logger.LogDebug("Creating new user entity for {UserId} while updating LastSeen.", userId);
            }

            user.LastSeen = DateTime.UtcNow;

            await context.SaveChangesAsync().ConfigureAwait(false);
            logger.LogDebug("Updated LastSeen for User {UserId}.", userId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating last seen for UserId {UserId}", userId);
        }
    }

    public async Task<UserEntity?> GetUserAsync(ulong userId)
    {
        await using var context = await dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        try
        {
            return await context.Users.FindAsync((decimal)userId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving user data for UserId {UserId}", userId);
            return null;
        }
    }
}