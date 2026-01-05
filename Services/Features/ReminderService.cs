using System.Globalization;
using Assistant.Net.Data;
using Assistant.Net.Data.Entities;
using Assistant.Net.Services.Data;
using Assistant.Net.Utilities;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Recognizers.Text;
using Microsoft.Recognizers.Text.DateTime;

namespace Assistant.Net.Services.Features;

public class ReminderService(
    IDbContextFactory<AssistantDbContext> dbFactory,
    DiscordSocketClient client,
    ILogger<ReminderService> logger,
    UserService userService,
    GuildService guildService)
{
    private readonly SemaphoreSlim _processingLock = new(1, 1);

    public async Task ProcessDueRemindersAsync()
    {
        if (!await _processingLock.WaitAsync(0)) return;

        try
        {
            var now = DateTime.UtcNow;
            await using var context = await dbFactory.CreateDbContextAsync().ConfigureAwait(false);

            var dueReminders = await GetDueRemindersAsync(context, now).ConfigureAwait(false);
            if (dueReminders.Count == 0) return;

            logger.LogInformation("Found {Count} due reminders.", dueReminders.Count);

            foreach (var reminder in dueReminders)
                await DispatchReminderAsync(context, reminder, now).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing due reminders.");
        }
        finally
        {
            _processingLock.Release();
        }
    }

    private async Task<List<ReminderEntity>> GetDueRemindersAsync(AssistantDbContext context, DateTime now)
    {
        try
        {
            return await context.Reminders
                .Where(r => r.TriggerTime <= now && r.IsActive)
                .ToListAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching due reminders from database.");
            return [];
        }
    }

    private async Task DispatchReminderAsync(AssistantDbContext context, ReminderEntity reminder, DateTime now)
    {
        try
        {
            var creator = await client.GetUserAsync((ulong)reminder.CreatorId).ConfigureAwait(false);
            var components = BuildReminderComponent(reminder, creator);

            if (reminder.IsDm)
                await SendDmReminderAsync(reminder, components).ConfigureAwait(false);
            else
                await SendChannelReminderAsync(reminder, components).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing reminder (ID: {Id}).", reminder.Id);
        }
        finally
        {
            await HandleReminderCompletionAsync(context, reminder, now).ConfigureAwait(false);
        }
    }

    private async Task SendDmReminderAsync(ReminderEntity reminder, MessageComponent components)
    {
        var targetId = (ulong)(reminder.TargetUserId ?? reminder.CreatorId);
        var user = await client.GetUserAsync(targetId).ConfigureAwait(false);

        if (user == null)
        {
            logger.LogWarning("Could not find user {TargetUserId} for DM reminder (ID: {Id}). Fallback to channel.",
                targetId, reminder.Id);
            await SendChannelReminderAsync(reminder, components, true).ConfigureAwait(false);
            return;
        }

        try
        {
            var dmChannel = await user.CreateDMChannelAsync().ConfigureAwait(false);
            await dmChannel.SendMessageAsync(components: components, flags: MessageFlags.ComponentsV2)
                .ConfigureAwait(false);
            logger.LogInformation("Sent DM reminder (ID: {Id}) to User {TargetUserId}", reminder.Id, targetId);
        }
        catch (HttpException hex) when (hex.DiscordCode == DiscordErrorCode.CannotSendMessageToUser)
        {
            logger.LogWarning(
                "Cannot send DM reminder to User {TargetUserId} (ID {Id}). Sending to original channel.",
                targetId, reminder.Id);
            await SendChannelReminderAsync(reminder, components, true).ConfigureAwait(false);
        }
    }

    private async Task SendChannelReminderAsync(ReminderEntity reminder, MessageComponent components,
        bool isFallback = false)
    {
        if (client.GetChannel((ulong)reminder.ChannelId) is not ITextChannel channel)
        {
            logger.LogWarning("Could not find channel {ChannelId} for reminder (ID: {Id}).",
                reminder.ChannelId, reminder.Id);
            return;
        }

        try
        {
            await channel.SendMessageAsync(components: components, allowedMentions: AllowedMentions.All,
                flags: MessageFlags.ComponentsV2).ConfigureAwait(false);

            if (!isFallback)
                logger.LogInformation(
                    "Sent channel reminder (ID: {Id}) to Channel {ChannelId} for User {TargetUserId}",
                    reminder.Id, reminder.ChannelId, reminder.TargetUserId ?? reminder.CreatorId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send {Type} channel reminder (ID: {Id}) to Channel {ChannelId}.",
                isFallback ? "fallback" : "standard", reminder.Id, reminder.ChannelId);
        }
    }

    private async Task HandleReminderCompletionAsync(AssistantDbContext context, ReminderEntity reminder,
        DateTime triggerTime)
    {
        if (string.IsNullOrEmpty(reminder.Recurrence) ||
            reminder.Recurrence.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            context.Reminders.Remove(reminder);
            await SaveChangesSafelyAsync(context, $"Deleted one-time reminder (ID: {reminder.Id}).");
        }
        else
        {
            var nextTrigger = CalculateNextTriggerTime(reminder.Recurrence, triggerTime);
            if (nextTrigger.HasValue)
            {
                reminder.LastTriggered = triggerTime;
                reminder.TriggerTime = nextTrigger.Value;
                await SaveChangesSafelyAsync(context,
                    $"Updated recurring reminder (ID: {reminder.Id}). Next trigger: {nextTrigger.Value}");
            }
            else
            {
                logger.LogWarning(
                    "Could not calculate next trigger time for reminder (ID: {Id}) with recurrence '{Recurrence}'. Deactivating.",
                    reminder.Id, reminder.Recurrence);
                reminder.LastTriggered = triggerTime;
                reminder.IsActive = false;
                await SaveChangesSafelyAsync(context, $"Deactivated invalid recurring reminder (ID: {reminder.Id}).");
            }
        }
    }

    private async Task SaveChangesSafelyAsync(AssistantDbContext context, string successLog)
    {
        try
        {
            await context.SaveChangesAsync().ConfigureAwait(false);
            logger.LogInformation("{Message}", successLog);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Database error while saving changes for reminder update/deletion.");
        }
    }

    private static MessageComponent BuildReminderComponent(ReminderEntity reminder, IUser? creator)
    {
        var titleText = string.IsNullOrWhiteSpace(reminder.Title) ? "⏰ Reminder" : $"⏰ Reminder: {reminder.Title}";

        var container = new ContainerBuilder()
            .WithTextDisplay(new TextDisplayBuilder($"# {titleText}"))
            .WithTextDisplay(new TextDisplayBuilder(reminder.Message))
            .WithSeparator();

        var creatorInfo = creator != null ? creator.Mention : $"<@{reminder.CreatorId}>";
        container.WithTextDisplay(
            new TextDisplayBuilder(
                $"**Set by:** {creatorInfo} | **Set at:** {reminder.CreatedAt.GetRelativeTime()}"));

        if (reminder.Recurrence != null)
            container.WithTextDisplay(new TextDisplayBuilder($"*This reminder repeats {reminder.Recurrence}*"));

        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    private DateTime? CalculateNextTriggerTime(string? recurrence, DateTime lastTriggered)
    {
        if (string.IsNullOrEmpty(recurrence)) return null;

        recurrence = recurrence.ToLowerInvariant();

        try
        {
            switch (recurrence)
            {
                case "daily": return lastTriggered.AddDays(1);
                case "weekly": return lastTriggered.AddDays(7);
                case "monthly": return lastTriggered.AddMonths(1);
                case "yearly": return lastTriggered.AddYears(1);
                case "hourly": return lastTriggered.AddHours(1);
                case "minutely": return lastTriggered.AddMinutes(1);
            }

            if (recurrence.StartsWith("every"))
            {
                var parts = recurrence.Split([' '], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 3 && int.TryParse(parts[1], out var count) && count > 0)
                {
                    var unit = parts[2];
                    if (unit.StartsWith("day")) return lastTriggered.AddDays(count);
                    if (unit.StartsWith("week")) return lastTriggered.AddDays(count * 7);
                    if (unit.StartsWith("month")) return lastTriggered.AddMonths(count);
                    if (unit.StartsWith("year")) return lastTriggered.AddYears(count);
                    if (unit.StartsWith("hour")) return lastTriggered.AddHours(count);
                    if (unit.StartsWith("minute")) return lastTriggered.AddMinutes(count);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError("Error calculating next trigger time for '{Recurrence}': {ExMessage}", recurrence,
                ex.Message);
            return null;
        }

        return null;
    }

    public async Task<ReminderEntity?> CreateReminderAsync(
        ulong creatorUserId,
        ulong guildId,
        ulong channelId,
        string message,
        DateTime triggerTime,
        bool isDm = true,
        ulong? targetUserId = null,
        string? recurrence = null,
        string? title = null)
    {
        await using var context = await dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        var dCreatorId = (decimal)creatorUserId;
        var dTargetId = targetUserId ?? dCreatorId;

        await userService.EnsureUserExistsAsync(context, creatorUserId).ConfigureAwait(false);
        await guildService.EnsureGuildExistsAsync(context, guildId).ConfigureAwait(false);
        if (dTargetId != dCreatorId)
            await userService.EnsureUserExistsAsync(context, (ulong)dTargetId).ConfigureAwait(false);

        await context.SaveChangesAsync().ConfigureAwait(false);

        try
        {
            var reminder = new ReminderEntity
            {
                CreatorId = dCreatorId,
                TargetUserId = dTargetId,
                ChannelId = channelId,
                GuildId = guildId,
                Message = message,
                TriggerTime = triggerTime.ToUniversalTime(),
                CreatedAt = DateTime.UtcNow,
                IsDm = isDm,
                Recurrence = recurrence?.ToLowerInvariant() == "none" ? null : recurrence?.ToLowerInvariant(),
                Title = title,
                IsActive = true,
                LastTriggered = null
            };

            context.Reminders.Add(reminder);
            await context.SaveChangesAsync().ConfigureAwait(false);

            logger.LogInformation("Created reminder (ID: {Id}) for User {TargetUserId}.", reminder.Id, dTargetId);
            return reminder;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create reminder for user {UserId}.", creatorUserId);
            return null;
        }
    }

    public async Task<List<ReminderEntity>> ListUserRemindersAsync(ulong userId)
    {
        await using var context = await dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        try
        {
            var decimalUserId = (decimal)userId;
            return await context.Reminders
                .Where(r => r.CreatorId == decimalUserId && r.IsActive && r.TriggerTime > DateTime.UtcNow)
                .OrderBy(r => r.TriggerTime)
                .ToListAsync()
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to list reminders for user {UserId}.", userId);
            return [];
        }
    }

    public async Task<(bool success, bool found)> CancelReminderAsync(ulong userId, int reminderId,
        bool deletePermanently = false)
    {
        await using var context = await dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        var decimalUserId = (decimal)userId;

        try
        {
            var reminder = await context.Reminders.FindAsync(reminderId).ConfigureAwait(false);

            if (reminder == null) return (false, false);
            if (reminder.CreatorId != decimalUserId) return (false, false); // Not owned by user

            if (deletePermanently)
            {
                context.Reminders.Remove(reminder);
                await context.SaveChangesAsync().ConfigureAwait(false);
                logger.LogInformation("Permanently deleted reminder (ID: {Id}).", reminderId);
                return (true, true);
            }

            if (!reminder.IsActive)
            {
                logger.LogInformation("Reminder (ID: {Id}) was already inactive.", reminderId);
                return (true, true);
            }

            reminder.IsActive = false;
            await context.SaveChangesAsync().ConfigureAwait(false);
            logger.LogInformation("Deactivated reminder (ID: {Id}).", reminderId);
            return (true, true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to cancel/delete reminder (ID: {Id}).", reminderId);
            return (false, true);
        }
    }

    public async Task<(bool success, bool found, ReminderEntity? updatedReminder)> EditReminderAsync(
        ulong userId,
        int reminderId,
        string? newMessage = null,
        DateTime? newTriggerTime = null,
        string? newRecurrence = null,
        string? newTitle = null)
    {
        await using var context = await dbFactory.CreateDbContextAsync().ConfigureAwait(false);
        var decimalUserId = (decimal)userId;

        try
        {
            var reminder = await context.Reminders.FindAsync(reminderId).ConfigureAwait(false);

            if (reminder == null) return (false, false, null);
            if (reminder.CreatorId != decimalUserId || !reminder.IsActive)
            {
                logger.LogWarning("Attempted to edit non-existent, inactive or unowned reminder (ID: {Id}).",
                    reminderId);
                return (false, false, null);
            }

            var hasChanges = false;

            if (newMessage != null)
            {
                reminder.Message = newMessage;
                hasChanges = true;
            }

            if (newTriggerTime.HasValue)
            {
                var utcTime = newTriggerTime.Value.ToUniversalTime();
                if (utcTime <= DateTime.UtcNow)
                {
                    logger.LogWarning("Attempted to set reminder time to the past for (ID: {Id}).", reminderId);
                    return (false, true, null);
                }

                reminder.TriggerTime = utcTime;
                reminder.LastTriggered = null;
                hasChanges = true;
            }

            if (newRecurrence != null)
            {
                reminder.Recurrence = newRecurrence.Equals("none", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : newRecurrence.ToLowerInvariant();
                reminder.LastTriggered = null;
                hasChanges = true;
            }

            if (newTitle != null)
            {
                reminder.Title = newTitle;
                hasChanges = true;
            }

            if (!hasChanges)
            {
                logger.LogInformation("No changes specified for editing reminder (ID: {Id}).", reminderId);
                return (true, true, reminder);
            }

            await context.SaveChangesAsync().ConfigureAwait(false);
            logger.LogInformation("Successfully edited reminder (ID: {Id}).", reminderId);
            return (true, true, reminder);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to edit reminder (ID: {Id}).", reminderId);
            return (false, true, null);
        }
    }

    public static DateTime? ParseTime(string timeString)
    {
        try
        {
            var referenceTime = DateTime.UtcNow;
            var results =
                DateTimeRecognizer.RecognizeDateTime(timeString, Culture.English, DateTimeOptions.None, referenceTime);
            if (results.Count == 0) return null;

            var resolutionValues = results[0].Resolution["values"] as List<Dictionary<string, string>> ?? [];

            DateTime? selectedDateTime = null;

            foreach (var value in resolutionValues)
                if (value.TryGetValue("type", out var type) && value.TryGetValue("value", out var dtString))
                    if (DateTime.TryParse(dtString, CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsedDt))
                    {
                        if (type == "time" && parsedDt.Date == DateTime.MinValue.Date)
                        {
                            var todayDt = referenceTime.Date + parsedDt.TimeOfDay;
                            if (todayDt > referenceTime)
                                parsedDt = todayDt;
                            else
                                parsedDt = referenceTime.Date.AddDays(1) + parsedDt.TimeOfDay;
                        }

                        if (parsedDt > referenceTime)
                        {
                            selectedDateTime = parsedDt;
                            break;
                        }

                        selectedDateTime ??= parsedDt;
                    }

            if (!selectedDateTime.HasValue || selectedDateTime.Value > referenceTime) return selectedDateTime;
            selectedDateTime = selectedDateTime.Value.AddDays(1);
            return selectedDateTime.Value <= referenceTime ? null : selectedDateTime;
        }
        catch (Exception)
        {
            return null;
        }
    }
}