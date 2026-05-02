using Assistant.Net.Data.Entities;

namespace Assistant.Net.Data.Repositories.Interfaces;

public interface IDmRelayRepository
{
    Task<DmRelayChannelEntity?> GetChannelAsync(ulong userId);
    Task<DmRelayChannelEntity?> GetChannelByDiscordIdAsync(ulong channelId);
    void AddChannel(DmRelayChannelEntity channel);

    Task<DmRelayMappingEntity?> GetMappingByRelayIdAsync(ulong relayMessageId);
    void AddMapping(DmRelayMappingEntity mapping);
}