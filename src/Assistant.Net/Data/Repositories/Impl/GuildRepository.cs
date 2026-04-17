using Assistant.Net.Data.Entities;
using Assistant.Net.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Net.Data.Repositories.Impl;

public class GuildRepository(AssistantDbContext context) : IGuildRepository
{
    public async Task EnsureExistsAsync(ulong guildId)
    {
        var exists = await context.Guilds.AnyAsync(g => g.Id == guildId).ConfigureAwait(false);
        if (!exists) context.Guilds.Add(new GuildEntity { Id = guildId });
    }
}