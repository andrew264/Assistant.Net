using Assistant.Net.Data.Entities;
using Assistant.Net.Data.Enums;
using Assistant.Net.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Net.Data.Repositories.Impl;

public class LoggingConfigRepository(AssistantDbContext context) : ILoggingConfigRepository
{
    public async Task<LogSettingsEntity?> GetAsync(ulong guildId, LogType logType)
    {
        return await context.LogSettings
            .FirstOrDefaultAsync(l => l.GuildId == guildId && l.LogType == logType)
            .ConfigureAwait(false);
    }

    public void Add(LogSettingsEntity config)
    {
        context.LogSettings.Add(config);
    }

    public async Task<int> ExecuteUpdateAsync(ulong guildId, LogType logType, bool isEnabled, ulong? channelId,
        int deleteDelayMs, DateTime updatedAt)
    {
        return await context.LogSettings
            .Where(l => l.GuildId == guildId && l.LogType == logType)
            .ExecuteUpdateAsync(s => s
                .SetProperty(l => l.IsEnabled, isEnabled)
                .SetProperty(l => l.ChannelId, channelId)
                .SetProperty(l => l.DeleteDelayMs, deleteDelayMs)
                .SetProperty(l => l.UpdatedAt, updatedAt))
            .ConfigureAwait(false);
    }
}