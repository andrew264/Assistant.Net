using Assistant.Net.Data.Entities;

namespace Assistant.Net.Data.Repositories.Interfaces;

public interface IReminderRepository
{
    Task<List<ReminderEntity>> GetActiveRemindersBeforeAsync(DateTime maxTime);
    Task<List<ReminderEntity>> GetUserRemindersAsync(ulong userId);
    Task<ReminderEntity?> GetAsync(ulong userId, int reminderId);
    Task<ReminderEntity?> GetByIdAsync(int reminderId);

    void Add(ReminderEntity reminder);
    void Remove(ReminderEntity reminder);

    Task<bool> ExistsAsync(int reminderId);

    Task<int> ExecuteUpdateAsync(
        ulong userId,
        int reminderId,
        string? newMessage = null,
        DateTime? newTriggerTime = null,
        string? newRecurrence = null,
        string? newTitle = null,
        ulong? newChannelId = null,
        ulong? newTargetUserId = null,
        bool? isActive = null);
}