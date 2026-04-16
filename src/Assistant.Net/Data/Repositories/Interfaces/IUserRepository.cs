using Assistant.Net.Data.Entities;

namespace Assistant.Net.Data.Repositories.Interfaces;

public interface IUserRepository
{
    Task EnsureExistsAsync(ulong userId);
    Task<UserEntity?> GetAsync(ulong userId);
    Task UpdateIntroductionAsync(ulong userId, string introduction);
    Task UpdateLastSeenAsync(ulong userId);
}