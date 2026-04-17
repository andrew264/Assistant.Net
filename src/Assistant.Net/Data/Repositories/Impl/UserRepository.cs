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