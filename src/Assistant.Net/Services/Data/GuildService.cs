using Assistant.Net.Data;
using Assistant.Net.Data.Entities;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Services.Data;

public class GuildService(ILogger<GuildService> logger)
{
    public async Task EnsureGuildExistsAsync(AssistantDbContext context, ulong guildId)
    {
        var decimalId = (decimal)guildId;
        if (await context.Guilds.FindAsync(decimalId).ConfigureAwait(false) == null)
        {
            context.Guilds.Add(new GuildEntity { Id = decimalId });
            logger.LogDebug("Added Guild {GuildId} to tracking context.", guildId);
        }
    }
}