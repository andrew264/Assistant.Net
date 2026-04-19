using Assistant.Net.Data.Entities;
using Assistant.Net.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Net.Data.Repositories.Impl;

public class GuildRepository(AssistantDbContext context) : IGuildRepository
{
    public async Task EnsureExistsAsync(ulong guildId)
    {
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO \"Guilds\" (\"Id\") VALUES ({guildId}) ON CONFLICT (\"Id\") DO NOTHING");
    }
}