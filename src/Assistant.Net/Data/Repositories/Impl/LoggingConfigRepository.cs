using Assistant.Net.Data.Entities;
using Assistant.Net.Data.Enums;
using Assistant.Net.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Net.Data.Repositories.Impl;

public class LoggingConfigRepository(AssistantDbContext context) : ILoggingConfigRepository
{
    public async Task<LogSettingsEntity?> GetAsync(ulong guildId, LogType logType)
    {
        var dGuildId = (decimal)guildId;
        return await context.LogSettings
            .FirstOrDefaultAsync(l => l.GuildId == dGuildId && l.LogType == logType)
            .ConfigureAwait(false);
    }

    public void Add(LogSettingsEntity config)
    {
        context.LogSettings.Add(config);
    }

    public async Task<int> ExecuteUpdateAsync(ulong guildId, LogType logType, bool isEnabled, ulong? channelId,
        int deleteDelayMs, DateTime updatedAt)
    {
        var dGuildId = (decimal)guildId;
        var dChannelId = (decimal?)channelId;

        return await context.LogSettings
            .Where(l => l.GuildId == dGuildId && l.LogType == logType)
            .ExecuteUpdateAsync(s => s
                .SetProperty(l => l.IsEnabled, isEnabled)
                .SetProperty(l => l.ChannelId, dChannelId)
                .SetProperty(l => l.DeleteDelayMs, deleteDelayMs)
                .SetProperty(l => l.UpdatedAt, updatedAt))
            .ConfigureAwait(false);
    }
}