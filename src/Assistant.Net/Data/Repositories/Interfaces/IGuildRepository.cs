namespace Assistant.Net.Data.Repositories.Interfaces;

public interface IGuildRepository
{
    Task EnsureExistsAsync(ulong guildId);
}