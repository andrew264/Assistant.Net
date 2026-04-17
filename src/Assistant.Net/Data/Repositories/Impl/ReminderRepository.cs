using Assistant.Net.Data.Entities;
using Assistant.Net.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Net.Data.Repositories.Impl;

public class ReminderRepository(AssistantDbContext context) : IReminderRepository
{
    public async Task<List<ReminderEntity>> GetActiveRemindersBeforeAsync(DateTime maxTime)
    {
        return await context.Reminders
            .Where(r => r.TriggerTime <= maxTime)
            .ToListAsync()
            .ConfigureAwait(false);
    }

    public async Task<List<ReminderEntity>> GetUserRemindersAsync(ulong userId)
    {
        return await context.Reminders
            .IgnoreQueryFilters()
            .Where(r => r.CreatorId == userId || r.TargetUserId == userId)
            .OrderBy(r => r.TriggerTime)
            .ToListAsync()
            .ConfigureAwait(false);
    }

    public async Task<ReminderEntity?> GetAsync(ulong userId, int reminderId)
    {
        return await context.Reminders
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == reminderId && (r.CreatorId == userId || r.TargetUserId == userId))
            .ConfigureAwait(false);
    }

    public async Task<ReminderEntity?> GetByIdAsync(int reminderId)
    {
        return await context.Reminders
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == reminderId)
            .ConfigureAwait(false);
    }

    public void Add(ReminderEntity reminder)
    {
        context.Reminders.Add(reminder);
    }

    public void Remove(ReminderEntity reminder)
    {
        context.Reminders.Remove(reminder);
    }

    public async Task<bool> ExistsAsync(int reminderId)
    {
        return await context.Reminders
            .IgnoreQueryFilters()
            .AnyAsync(r => r.Id == reminderId)
            .ConfigureAwait(false);
    }

    public async Task<int> ExecuteUpdateAsync(
        ulong userId,
        int reminderId,
        string? newMessage = null,
        DateTime? newTriggerTime = null,
        string? newRecurrence = null,
        string? newTitle = null,
        ulong? newChannelId = null,
        ulong? newTargetUserId = null,
        bool? isActive = null)
    {
        return await context.Reminders
            .IgnoreQueryFilters()
            .Where(r => r.Id == reminderId && (r.CreatorId == userId || r.TargetUserId == userId))
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Message, b => newMessage ?? b.Message)
                .SetProperty(r => r.Title, b => newTitle ?? b.Title)
                .SetProperty(r => r.Recurrence, b => newRecurrence ?? b.Recurrence)
                .SetProperty(r => r.IsActive, b => isActive ?? b.IsActive)
                .SetProperty(r => r.TriggerTime,
                    b => newTriggerTime.HasValue ? newTriggerTime.Value.ToUniversalTime() : b.TriggerTime)
                .SetProperty(r => r.LastTriggered, b => newTriggerTime.HasValue ? null : b.LastTriggered)
                .SetProperty(r => r.ChannelId, b => newChannelId ?? b.ChannelId)
                .SetProperty(r => r.TargetUserId, b => newTargetUserId ?? b.TargetUserId))
            .ConfigureAwait(false);
    }
}