using Assistant.Net.Data.Entities;
using Assistant.Net.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Net.Data.Repositories.Impl;

public class GuildRepository(AssistantDbContext context) : IGuildRepository
{
    public async Task EnsureExistsAsync(ulong guildId)
    {
        var decimalId = (decimal)guildId;
        var exists = await context.Guilds.AnyAsync(g => g.Id == decimalId).ConfigureAwait(false);
        if (!exists) context.Guilds.Add(new GuildEntity { Id = decimalId });
    }
}