using Assistant.Net.Data.Entities;
using Assistant.Net.Data.Repositories.Interfaces;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Services.Data;

public class UserService(IUnitOfWorkFactory uowFactory, ILogger<UserService> logger)
{
    public async Task UpdateUserIntroductionAsync(ulong userId, string introduction)
    {
        await using var uow = await uowFactory.CreateAsync().ConfigureAwait(false);
        try
        {
            var user = await uow.Users.GetAsync(userId).ConfigureAwait(false);

            if (user?.About == introduction)
            {
                logger.LogInformation("Introduction for User {UserId} submitted, but no changes detected.", userId);
                return;
            }

            await uow.Users.UpdateIntroductionAsync(userId, introduction).ConfigureAwait(false);
            await uow.SaveChangesAsync().ConfigureAwait(false);
            logger.LogInformation("Successfully updated introduction for User {UserId}.", userId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating user introduction for UserId {UserId}", userId);
            throw;
        }
    }

    public async Task UpdateLastSeenAsync(ulong userId)
    {
        await using var uow = await uowFactory.CreateAsync().ConfigureAwait(false);
        try
        {
            await uow.Users.UpdateLastSeenAsync(userId).ConfigureAwait(false);
            await uow.SaveChangesAsync().ConfigureAwait(false);
            logger.LogDebug("Updated LastSeen for User {UserId}.", userId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating last seen for UserId {UserId}", userId);
        }
    }

    public async Task<UserEntity?> GetUserAsync(ulong userId)
    {
        await using var uow = await uowFactory.CreateAsync().ConfigureAwait(false);
        try
        {
            return await uow.Users.GetAsync(userId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving user data for UserId {UserId}", userId);
            return null;
        }
    }
}