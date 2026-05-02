using Assistant.Net.Data.Entities;
using Assistant.Net.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Net.Data.Repositories.Impl;

public class DmRelayRepository(AssistantDbContext context) : IDmRelayRepository
{
    public async Task<DmRelayChannelEntity?> GetChannelAsync(ulong userId) =>
        await context.DmRelayChannels.FindAsync(userId).ConfigureAwait(false);

    public async Task<DmRelayChannelEntity?> GetChannelByDiscordIdAsync(ulong channelId)
    {
        return await context.DmRelayChannels.FirstOrDefaultAsync(c => c.ChannelId == channelId).ConfigureAwait(false);
    }

    public void AddChannel(DmRelayChannelEntity channel)
    {
        context.DmRelayChannels.Add(channel);
    }

    public async Task<DmRelayMappingEntity?> GetMappingByRelayIdAsync(ulong relayMessageId)
    {
        return await context.DmRelayMappings
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.RelayMessageId == relayMessageId)
            .ConfigureAwait(false);
    }

    public void AddMapping(DmRelayMappingEntity mapping)
    {
        context.DmRelayMappings.Add(mapping);
    }
}