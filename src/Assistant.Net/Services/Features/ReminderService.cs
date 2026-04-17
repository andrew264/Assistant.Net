using System.Globalization;
using Assistant.Net.Data.Entities;
using Assistant.Net.Data.Repositories.Interfaces;
using Assistant.Net.Utilities;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Recognizers.Text;
using Microsoft.Recognizers.Text.DateTime;

namespace Assistant.Net.Services.Features;

public class ReminderService(
    IUnitOfWorkFactory uowFactory,
    DiscordSocketClient client,
    ILogger<ReminderService> logger)
{
    private readonly Lock _lock = new();
    private readonly PriorityQueue<int, DateTime> _queue = new();
    private bool _isInitialized;
    private CancellationTokenSource _waitCanceller = new();

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        logger.LogInformation("Initializing ReminderService: Loading active reminders...");

        await using var uow = await uowFactory.CreateAsync().ConfigureAwait(false);
        var reminders = await uow.Reminders.GetActiveRemindersBeforeAsync(DateTime.MaxValue).ConfigureAwait(false);

        lock (_lock)
        {
            foreach (var r in reminders.Where(r => r.TriggerTime > DateTime.UtcNow))
                _queue.Enqueue(r.Id, r.TriggerTime);
            _isInitialized = true;
        }

        logger.LogInformation("Loaded {Count} reminders into memory.", reminders.Count);
        InterruptWait();
    }

    public async Task WaitForNextTickAsync(CancellationToken stoppingToken)
    {
        int? reminderId = null;
        var triggerTime = DateTime.MaxValue;
        CancellationToken waitToken;

        lock (_lock)
        {
            if (_queue.TryPeek(out _, out var time))
                if (_queue.TryDequeue(out var id, out _))
                {
                    reminderId = id;
                    triggerTime = time;
                }

            waitToken = _waitCanceller.Token;
        }

        if (reminderId == null)
        {
            try
            {
                await Task.Delay(Timeout.Infinite, waitToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            return;
        }

        var delay = triggerTime - DateTime.UtcNow;
        if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;

        try
        {
            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(waitToken, stoppingToken);
            await Task.Delay(delay, linkedSource.Token).ConfigureAwait(false);

            await ProcessReminderAsync(reminderId.Value, triggerTime).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (stoppingToken.IsCancellationRequested) return;
            lock (_lock)
            {
                _queue.Enqueue(reminderId.Value, triggerTime);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during reminder wait loop.");
        }
    }

    private void InterruptWait()
    {
        lock (_lock)
        {
            _waitCanceller.Cancel();
            _waitCanceller = new CancellationTokenSource();
        }
    }

    private async Task ProcessReminderAsync(int reminderId, DateTime expectedTriggerTime)
    {
        await using var uow = await uowFactory.CreateAsync().ConfigureAwait(false);
        var reminder = await uow.Reminders.GetByIdAsync(reminderId).ConfigureAwait(false);

        if (reminder is not { IsActive: true }) return;
        if (reminder.TriggerTime > expectedTriggerTime.AddSeconds(5)) return;

        try
        {
            await DispatchReminderAsync(uow, reminder, DateTime.UtcNow).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing reminder {Id}.", reminderId);
        }
    }

    private async Task DispatchReminderAsync(IUnitOfWork uow, ReminderEntity reminder, DateTime now)
    {
        try
        {
            var creator = await client.GetUserAsync(reminder.CreatorId).ConfigureAwait(false);
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
            await HandleReminderCompletionAsync(uow, reminder, now).ConfigureAwait(false);
        }
    }

    private async Task HandleReminderCompletionAsync(IUnitOfWork uow, ReminderEntity reminder, DateTime triggerTime)
    {
        if (string.IsNullOrEmpty(reminder.Recurrence) ||
            reminder.Recurrence.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            uow.Reminders.Remove(reminder);
            await SaveChangesSafelyAsync(uow, $"Deleted one-time reminder (ID: {reminder.Id}).");
        }
        else
        {
            var nextTrigger = CalculateNextTriggerTime(reminder.Recurrence, triggerTime);
            if (nextTrigger.HasValue)
            {
                reminder.LastTriggered = triggerTime;
                reminder.TriggerTime = nextTrigger.Value;
                await SaveChangesSafelyAsync(uow,
                    $"Updated recurring reminder (ID: {reminder.Id}). Next trigger: {nextTrigger.Value}");

                lock (_lock)
                {
                    _queue.Enqueue(reminder.Id, reminder.TriggerTime);
                    InterruptWait();
                }
            }
            else
            {
                logger.LogWarning("Could not calculate next trigger time for reminder (ID: {Id}). Deactivating.",
                    reminder.Id);
                reminder.LastTriggered = triggerTime;
                reminder.IsActive = false;
                await SaveChangesSafelyAsync(uow, $"Deactivated invalid recurring reminder (ID: {reminder.Id}).");
            }
        }
    }

    public async Task<ReminderEntity?> CreateReminderAsync(ulong creatorUserId, ulong guildId, ulong channelId,
        string message, DateTime triggerTime, bool isDm = true, ulong? targetUserId = null, string? recurrence = null,
        string? title = null)
    {
        await using var uow = await uowFactory.CreateAsync().ConfigureAwait(false);
        var targetId = targetUserId ?? creatorUserId;

        await uow.Users.EnsureExistsAsync(creatorUserId).ConfigureAwait(false);
        await uow.Guilds.EnsureExistsAsync(guildId).ConfigureAwait(false);
        if (targetId != creatorUserId) await uow.Users.EnsureExistsAsync(targetId).ConfigureAwait(false);

        try
        {
            var reminder = new ReminderEntity
            {
                CreatorId = creatorUserId,
                TargetUserId = targetId,
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

            uow.Reminders.Add(reminder);
            await uow.SaveChangesAsync().ConfigureAwait(false);

            lock (_lock)
            {
                _queue.Enqueue(reminder.Id, reminder.TriggerTime);
            }

            InterruptWait();

            logger.LogInformation("Created reminder (ID: {Id}) for User {TargetUserId}.", reminder.Id, targetId);
            return reminder;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create reminder for user {UserId}.", creatorUserId);
            return null;
        }
    }

    public async Task<(bool success, bool found, ReminderEntity? updatedReminder)> EditReminderAsync(ulong userId,
        int reminderId, string? newMessage = null, DateTime? newTriggerTime = null, string? newRecurrence = null,
        string? newTitle = null, ulong? newChannelId = null, ulong? newTargetUserId = null, bool? isActive = null)
    {
        await using var uow = await uowFactory.CreateAsync().ConfigureAwait(false);

        if (!await uow.Reminders.ExistsAsync(reminderId).ConfigureAwait(false)) return (false, false, null);

        var rowsAffected = await uow.Reminders.ExecuteUpdateAsync(userId, reminderId, newMessage, newTriggerTime,
            newRecurrence, newTitle, newChannelId, newTargetUserId, isActive).ConfigureAwait(false);

        if (rowsAffected == 0) return (false, true, null);

        var reminder = await uow.Reminders.GetByIdAsync(reminderId).ConfigureAwait(false);

        try
        {
            if (reminder == null || (!newTriggerTime.HasValue && isActive != true)) return (true, true, reminder);
            lock (_lock)
            {
                _queue.Enqueue(reminder.Id, reminder.TriggerTime);
            }

            InterruptWait();
            return (true, true, reminder);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to edit reminder (ID: {Id}).", reminderId);
            return (false, true, null);
        }
    }

    public async Task<(bool success, bool found)> DeleteReminderAsync(ulong userId, int reminderId)
    {
        await using var uow = await uowFactory.CreateAsync().ConfigureAwait(false);
        try
        {
            var reminder = await uow.Reminders.GetAsync(userId, reminderId).ConfigureAwait(false);
            if (reminder == null) return (false, false);

            uow.Reminders.Remove(reminder);
            await uow.SaveChangesAsync().ConfigureAwait(false);
            return (true, true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete reminder (ID: {Id}).", reminderId);
            return (false, true);
        }
    }

    public async Task<ReminderEntity?> GetReminderAsync(ulong userId, int reminderId)
    {
        await using var uow = await uowFactory.CreateAsync().ConfigureAwait(false);
        return await uow.Reminders.GetAsync(userId, reminderId).ConfigureAwait(false);
    }

    public async Task<List<ReminderEntity>> ListUserRemindersAsync(ulong userId)
    {
        await using var uow = await uowFactory.CreateAsync().ConfigureAwait(false);
        try
        {
            return await uow.Reminders.GetUserRemindersAsync(userId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to list reminders for user {UserId}.", userId);
            return [];
        }
    }

    private async Task SaveChangesSafelyAsync(IUnitOfWork uow, string successLog)
    {
        try
        {
            await uow.SaveChangesAsync().ConfigureAwait(false);
            logger.LogInformation("{Message}", successLog);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Database error while saving changes for reminder update/deletion.");
        }
    }

    private async Task SendDmReminderAsync(ReminderEntity reminder, MessageComponent components)
    {
        var targetId = reminder.TargetUserId ?? reminder.CreatorId;
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
            logger.LogWarning("Cannot send DM reminder to User {TargetUserId} (ID {Id}). Sending to original channel.",
                targetId, reminder.Id);
            await SendChannelReminderAsync(reminder, components, true).ConfigureAwait(false);
        }
    }

    private async Task SendChannelReminderAsync(ReminderEntity reminder, MessageComponent components,
        bool isFallback = false)
    {
        if (client.GetChannel(reminder.ChannelId) is not ITextChannel channel)
        {
            logger.LogWarning("Could not find channel {ChannelId} for reminder (ID: {Id}).", reminder.ChannelId,
                reminder.Id);
            return;
        }

        try
        {
            await channel.SendMessageAsync(components: components, allowedMentions: AllowedMentions.All,
                flags: MessageFlags.ComponentsV2).ConfigureAwait(false);
            if (!isFallback)
                logger.LogInformation("Sent channel reminder (ID: {Id}) to Channel {ChannelId}", reminder.Id,
                    reminder.ChannelId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send {Type} channel reminder (ID: {Id}).",
                isFallback ? "fallback" : "standard", reminder.Id);
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
            new TextDisplayBuilder($"**Set by:** {creatorInfo} {reminder.CreatedAt.GetRelativeTime()}"));

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
        }

        return null;
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
                            parsedDt = todayDt > referenceTime
                                ? todayDt
                                : referenceTime.Date.AddDays(1) + parsedDt.TimeOfDay;
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
        catch
        {
            return null;
        }
    }
}