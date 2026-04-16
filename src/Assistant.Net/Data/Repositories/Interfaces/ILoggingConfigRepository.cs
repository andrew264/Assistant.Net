using Assistant.Net.Data.Entities;
using Assistant.Net.Data.Enums;

namespace Assistant.Net.Data.Repositories.Interfaces;

public interface ILoggingConfigRepository
{
    Task<LogSettingsEntity?> GetAsync(ulong guildId, LogType logType);
    void Add(LogSettingsEntity config);

    Task<int> ExecuteUpdateAsync(ulong guildId, LogType logType, bool isEnabled, ulong? channelId, int deleteDelayMs,
        DateTime updatedAt);
}