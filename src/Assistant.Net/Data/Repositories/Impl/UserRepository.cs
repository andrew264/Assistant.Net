using Assistant.Net.Data.Entities;
using Assistant.Net.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Net.Data.Repositories.Impl;

public class UserRepository(AssistantDbContext context) : IUserRepository
{
    public async Task EnsureExistsAsync(ulong userId)
    {
        var exists = await context.Users.AnyAsync(u => u.Id == userId).ConfigureAwait(false);
        if (!exists) context.Users.Add(new UserEntity { Id = userId });
    }

    public async Task EnsureUsersExistAsync(IEnumerable<ulong> userIds)
    {
        var distinctIds = userIds.Distinct().ToList();

        var existingIds = await context.Users
            .Where(u => distinctIds.Contains(u.Id))
            .Select(u => u.Id)
            .ToListAsync()
            .ConfigureAwait(false);

        var missingIds = distinctIds.Except(existingIds).ToList();

        if (missingIds.Count > 0) context.Users.AddRange(missingIds.Select(id => new UserEntity { Id = id }));
    }

    public async Task<UserEntity?> GetAsync(ulong userId) =>
        await context.Users.FindAsync(userId).ConfigureAwait(false);

    public async Task UpdateIntroductionAsync(ulong userId, string introduction)
    {
        var affected = await context.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.About, introduction))
            .ConfigureAwait(false);

        if (affected == 0) context.Users.Add(new UserEntity { Id = userId, About = introduction });
    }

    public async Task UpdateLastSeenAsync(ulong userId)
    {
        var now = DateTime.UtcNow;
        var affected = await context.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastSeen, now))
            .ConfigureAwait(false);

        if (affected == 0) context.Users.Add(new UserEntity { Id = userId, LastSeen = now });
    }
}