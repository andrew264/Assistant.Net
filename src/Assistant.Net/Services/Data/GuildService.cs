using Assistant.Net.Data.Repositories.Interfaces;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Services.Data;

public class GuildService(IUnitOfWorkFactory uowFactory, ILogger<GuildService> logger)
{
    public async Task EnsureGuildExistsAsync(ulong guildId)
    {
        await using var uow = await uowFactory.CreateAsync().ConfigureAwait(false);
        await uow.Guilds.EnsureExistsAsync(guildId).ConfigureAwait(false);
        await uow.SaveChangesAsync().ConfigureAwait(false);
        logger.LogDebug("Checked existence/Added Guild {GuildId} to tracking context.", guildId);
    }
}