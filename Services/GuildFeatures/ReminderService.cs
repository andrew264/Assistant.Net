using System.Globalization;
using Assistant.Net.Models.Reminder;
using Assistant.Net.Utilities;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Recognizers.Text;
using Microsoft.Recognizers.Text.DateTime;
using MongoDB.Driver;
using MongoDB.Driver.Linq;

namespace Assistant.Net.Services.GuildFeatures;

public class ReminderService : IHostedService, IDisposable
{
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(60); // Check every 60 seconds
    private readonly DiscordSocketClient _client;
    private readonly IMongoCollection<CounterModel> _countersCollection;
    private readonly ILogger<ReminderService> _logger;
    private readonly IMongoCollection<ReminderModel> _reminderCollection;
    private Timer? _timer;

    public ReminderService(
        IMongoDatabase database,
        DiscordSocketClient client,
        ILogger<ReminderService> logger)
    {
        _reminderCollection = database.GetCollection<ReminderModel>("reminders");
        _countersCollection = database.GetCollection<CounterModel>("counters");
        _client = client;
        _logger = logger;

        EnsureIndexesAsync().ConfigureAwait(false).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _timer?.Dispose();
        GC.SuppressFinalize(this);
    }


    // --- Hosted Service Implementation ---
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("ReminderService is starting.");
        _timer = new Timer(DoWork, null, TimeSpan.Zero, _checkInterval);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("ReminderService is stopping.");
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    // --- Index Creation ---
    private async Task EnsureIndexesAsync()
    {
        try
        {
            var dueReminderIndex = Builders<ReminderModel>.IndexKeys
                .Ascending(r => r.IsActive)
                .Ascending(r => r.TriggerTime);
            await _reminderCollection.Indexes.CreateOneAsync(
                new CreateIndexModel<ReminderModel>(dueReminderIndex,
                    new CreateIndexOptions { Name = "ActiveTriggerTime" })
            ).ConfigureAwait(false);

            var userReminderIndex = Builders<ReminderModel>.IndexKeys
                .Ascending(r => r.Id.UserId)
                .Ascending(r => r.IsActive)
                .Ascending(r => r.TriggerTime);
            await _reminderCollection.Indexes.CreateOneAsync(
                new CreateIndexModel<ReminderModel>(userReminderIndex,
                    new CreateIndexOptions { Name = "UserIdActiveTriggerTime" })
            ).ConfigureAwait(false);

            _logger.LogInformation("Ensured indexes on reminders collection.");
        }
        catch (MongoCommandException ex) when (ex.CodeName == "IndexOptionsConflict" ||
                                               ex.CodeName == "IndexKeySpecsConflict" ||
                                               ex.Message.Contains("already exists with different options"))
        {
            _logger.LogWarning(
                "One or more reminder indexes already exist with potentially different options: {ErrorMessage}. This might be okay if definitions match.",
                ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure indexes on reminders collection.");
        }
    }

    // --- Background Work ---
    private void DoWork(object? state)
    {
        if (_client.ConnectionState != ConnectionState.Connected || _client.LoginState != LoginState.LoggedIn)
        {
            _logger.LogTrace("ReminderService skipping check, client not ready.");
            return;
        }

        _ = CheckRemindersAsync();
    }

    private async Task CheckRemindersAsync()
    {
        _logger.LogTrace("Checking for due reminders...");
        var now = DateTime.UtcNow;

        List<ReminderModel> dueReminders;
        try
        {
            dueReminders = await _reminderCollection.AsQueryable().Where(r => r.TriggerTime <= now && r.IsActive)
                .ToListAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching due reminders from database.");
            return;
        }


        if (dueReminders.Count > 0) _logger.LogInformation("Found {Count} due reminders.", dueReminders.Count);

        foreach (var reminder in dueReminders)
            try
            {
                var creatorUser = await _client.GetUserAsync(reminder.Id.UserId).ConfigureAwait(false);
                var components = BuildReminderComponent(reminder, creatorUser);
                var targetId = reminder.TargetUserId ?? reminder.Id.UserId;

                if (reminder.IsDm)
                {
                    var user = await _client.GetUserAsync(targetId).ConfigureAwait(false);
                    if (user != null)
                    {
                        try
                        {
                            var dmChannel = await user.CreateDMChannelAsync().ConfigureAwait(false);
                            await dmChannel.SendMessageAsync(components: components, flags: MessageFlags.ComponentsV2)
                                .ConfigureAwait(false);
                            _logger.LogInformation("Sent DM reminder (ID: {UserId}/{Seq}) to User {TargetUserId}",
                                reminder.Id.UserId, reminder.Id.SequenceNumber, targetId);
                        }
                        catch (HttpException hex) when (hex.DiscordCode == DiscordErrorCode.CannotSendMessageToUser)
                        {
                            _logger.LogWarning(
                                "Cannot send DM reminder to User {TargetUserId} (User {UserId}/{Seq}). Sending to original channel.",
                                targetId, reminder.Id.UserId, reminder.Id.SequenceNumber);
                            await SendToChannelFallback(reminder, components).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Could not find user {TargetUserId} for DM reminder (ID: {UserId}/{Seq}).",
                            targetId, reminder.Id.UserId, reminder.Id.SequenceNumber);
                        await SendToChannelFallback(reminder, components).ConfigureAwait(false);
                    }
                }
                else
                {
                    await SendToChannel(reminder, components).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing reminder (ID: {UserId}/{Seq}).", reminder.Id.UserId,
                    reminder.Id.SequenceNumber);
            }
            finally
            {
                await HandleReminderCompletion(reminder, now).ConfigureAwait(false);
            }

        _logger.LogTrace("Finished checking reminders.");
    }

    private static MessageComponent BuildReminderComponent(ReminderModel reminder, IUser? creator)
    {
        var titleText = string.IsNullOrWhiteSpace(reminder.Title) ? "⏰ Reminder" : $"⏰ Reminder: {reminder.Title}";

        var container = new ContainerBuilder()
            .WithTextDisplay(new TextDisplayBuilder($"# {titleText}"))
            .WithTextDisplay(new TextDisplayBuilder(reminder.Message))
            .WithSeparator();

        var creatorInfo = creator != null ? creator.Mention : $"<@{reminder.Id.UserId}>";
        container.WithTextDisplay(
            new TextDisplayBuilder(
                $"**Set by:** {creatorInfo} | **Set at:** {reminder.CreationTime.GetRelativeTime()}"));

        if (reminder.Recurrence != null)
            container.WithTextDisplay(new TextDisplayBuilder($"*This reminder repeats {reminder.Recurrence}*"));

        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    private async Task SendToChannel(ReminderModel reminder, MessageComponent components)
    {
        if (_client.GetChannel(reminder.ChannelId) is ITextChannel channel)
            try
            {
                await channel.SendMessageAsync(components: components, allowedMentions: AllowedMentions.All,
                    flags: MessageFlags.ComponentsV2).ConfigureAwait(false);
                _logger.LogInformation(
                    "Sent channel reminder (ID: {UserId}/{Seq}) to Channel {ChannelId} for User {TargetUserId}",
                    reminder.Id.UserId, reminder.Id.SequenceNumber, reminder.ChannelId,
                    reminder.TargetUserId ?? reminder.Id.UserId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send channel reminder (ID: {UserId}/{Seq}) to Channel {ChannelId}.",
                    reminder.Id.UserId, reminder.Id.SequenceNumber, reminder.ChannelId);
            }
        else
            _logger.LogWarning("Could not find channel {ChannelId} for channel reminder (ID: {UserId}/{Seq}).",
                reminder.ChannelId, reminder.Id.UserId, reminder.Id.SequenceNumber);
    }

    private async Task SendToChannelFallback(ReminderModel reminder, MessageComponent components)
    {
        if (_client.GetChannel(reminder.ChannelId) is ITextChannel channel)
            try
            {
                await channel.SendMessageAsync(components: components,
                    allowedMentions: AllowedMentions.All, flags: MessageFlags.ComponentsV2).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to send fallback channel reminder (ID: {UserId}/{Seq}) to Channel {ChannelId}.",
                    reminder.Id.UserId, reminder.Id.SequenceNumber, reminder.ChannelId);
            }
        else
            _logger.LogWarning(
                "Could not find original channel {ChannelId} for fallback reminder (ID: {UserId}/{Seq}).",
                reminder.ChannelId, reminder.Id.UserId, reminder.Id.SequenceNumber);
    }


    private async Task HandleReminderCompletion(ReminderModel reminder, DateTime triggerTime)
    {
        if (string.IsNullOrEmpty(reminder.Recurrence) ||
            reminder.Recurrence.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await _reminderCollection.DeleteOneAsync(r => r.Id == reminder.Id).ConfigureAwait(false);
                _logger.LogInformation("Deleted one-time reminder (ID: {UserId}/{Seq}).", reminder.Id.UserId,
                    reminder.Id.SequenceNumber);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete reminder (ID: {UserId}/{Seq}).", reminder.Id.UserId,
                    reminder.Id.SequenceNumber);
            }
        }
        else
        {
            var nextTrigger = CalculateNextTriggerTime(reminder.Recurrence, triggerTime);
            if (nextTrigger.HasValue)
            {
                var updateFilter = Builders<ReminderModel>.Filter.Eq(r => r.Id, reminder.Id);
                var update = Builders<ReminderModel>.Update
                    .Set(r => r.LastTriggered, triggerTime)
                    .Set(r => r.TriggerTime, nextTrigger.Value);
                try
                {
                    await _reminderCollection.UpdateOneAsync(updateFilter, update).ConfigureAwait(false);
                    _logger.LogInformation(
                        "Updated recurring reminder (ID: {UserId}/{Seq}). Next trigger: {NextTrigger}",
                        reminder.Id.UserId, reminder.Id.SequenceNumber, nextTrigger.Value);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to update recurring reminder (ID: {UserId}/{Seq}).",
                        reminder.Id.UserId, reminder.Id.SequenceNumber);
                }
            }
            else
            {
                _logger.LogWarning(
                    "Could not calculate next trigger time for reminder (ID: {UserId}/{Seq}) with recurrence '{Recurrence}'. Deactivating.",
                    reminder.Id.UserId, reminder.Id.SequenceNumber, reminder.Recurrence);
                var updateFilter = Builders<ReminderModel>.Filter.Eq(r => r.Id, reminder.Id);
                var update = Builders<ReminderModel>.Update
                    .Set(r => r.LastTriggered, triggerTime)
                    .Set(r => r.IsActive, false); // Deactivate
                await _reminderCollection.UpdateOneAsync(updateFilter, update).ConfigureAwait(false);
            }
        }
    }

    // --- Recurrence Calculation ---
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

            if (recurrence.StartsWith("every")) // "every 2 days", "every 3 weeks" etc.
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
            _logger.LogError("Error calculating next trigger time for '{Recurrence}': {ExMessage}", recurrence,
                ex.Message);
            return null;
        }

        return null;
    }

    // --- Sequence Number Generation ---
    private async Task<int> GetNextSequenceNumberAsync(ulong userId)
    {
        var counterId = $"reminder_user_{userId}";
        var filter = Builders<CounterModel>.Filter.Eq(c => c.Id, counterId);
        var update = Builders<CounterModel>.Update.Inc(c => c.SequenceValue, 1);
        var options = new FindOneAndUpdateOptions<CounterModel, CounterModel>
        {
            IsUpsert = true,
            ReturnDocument = ReturnDocument.After
        };

        try
        {
            var result = await _countersCollection.FindOneAndUpdateAsync(filter, update, options).ConfigureAwait(false);
            return result.SequenceValue;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get next sequence number for user {UserId}", userId);
            throw;
        }
    }

    // --- Public Methods for Module ---

    public async Task<ReminderModel?> CreateReminderAsync(
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
        try
        {
            var sequenceNumber = await GetNextSequenceNumberAsync(creatorUserId).ConfigureAwait(false);
            var reminder = new ReminderModel
            {
                Id = new ReminderIdKey { UserId = creatorUserId, SequenceNumber = sequenceNumber },
                TargetUserId = targetUserId ?? creatorUserId,
                ChannelId = channelId,
                GuildId = guildId,
                Message = message,
                TriggerTime = triggerTime.ToUniversalTime(), // Ensure UTC
                CreationTime = DateTime.UtcNow,
                IsDm = isDm,
                Recurrence = recurrence?.ToLowerInvariant() == "none" ? null : recurrence?.ToLowerInvariant(),
                Title = title,
                IsActive = true,
                LastTriggered = null
            };

            await _reminderCollection.InsertOneAsync(reminder).ConfigureAwait(false);
            _logger.LogInformation("Created reminder (ID: {UserId}/{Seq}) for User {TargetUserId}.", creatorUserId,
                sequenceNumber, reminder.TargetUserId);
            return reminder;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create reminder for user {UserId}.", creatorUserId);
            return null;
        }
    }

    public async Task<List<ReminderModel>> ListUserRemindersAsync(ulong userId)
    {
        try
        {
            var filter = Builders<ReminderModel>.Filter.And(
                Builders<ReminderModel>.Filter.Eq(r => r.Id.UserId, userId),
                Builders<ReminderModel>.Filter.Eq(r => r.IsActive, true),
                Builders<ReminderModel>.Filter.Gt(r => r.TriggerTime, DateTime.UtcNow) // Only future reminders
            );
            var sort = Builders<ReminderModel>.Sort.Ascending(r => r.TriggerTime);

            return await _reminderCollection.Find(filter).Sort(sort).ToListAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list reminders for user {UserId}.", userId);
            return []; // Return empty list on error
        }
    }

    public async Task<(bool success, bool found)> CancelReminderAsync(ulong userId, int sequenceNumber,
        bool deletePermanently = false)
    {
        var reminderId = new ReminderIdKey { UserId = userId, SequenceNumber = sequenceNumber };
        var filter = Builders<ReminderModel>.Filter.Eq(r => r.Id, reminderId);

        try
        {
            if (deletePermanently)
            {
                var result = await _reminderCollection.DeleteOneAsync(filter).ConfigureAwait(false);
                if (result.DeletedCount > 0)
                {
                    _logger.LogInformation("Permanently deleted reminder (ID: {UserId}/{Seq}).", userId,
                        sequenceNumber);
                    return (true, true);
                }

                _logger.LogWarning("Attempted to delete non-existent or already deleted reminder (ID: {UserId}/{Seq}).",
                    userId, sequenceNumber);
            }
            else
            {
                var update = Builders<ReminderModel>.Update.Set(r => r.IsActive, false);
                var result = await _reminderCollection.UpdateOneAsync(filter, update).ConfigureAwait(false);
                if (result.MatchedCount > 0)
                {
                    if (result.ModifiedCount > 0)
                    {
                        _logger.LogInformation("Deactivated reminder (ID: {UserId}/{Seq}).", userId, sequenceNumber);
                        return (true, true);
                    }

                    _logger.LogInformation("Reminder (ID: {UserId}/{Seq}) was already inactive.", userId,
                        sequenceNumber);
                    return (true, true);
                }

                _logger.LogWarning("Attempted to deactivate non-existent reminder (ID: {UserId}/{Seq}).", userId,
                    sequenceNumber);
            }

            return (false, false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel/delete reminder (ID: {UserId}/{Seq}). DeletePermanently={Delete}",
                userId, sequenceNumber, deletePermanently);
            return (false, true);
        }
    }

    public async Task<(bool success, bool found, ReminderModel? updatedReminder)> EditReminderAsync(
        ulong userId,
        int sequenceNumber,
        string? newMessage = null,
        DateTime? newTriggerTime = null,
        string? newRecurrence = null,
        string? newTitle = null)
    {
        var reminderId = new ReminderIdKey { UserId = userId, SequenceNumber = sequenceNumber };
        var filter = Builders<ReminderModel>.Filter.Eq(r => r.Id, reminderId);

        // Fetch the existing reminder first to validate ownership and active status
        ReminderModel? existingReminder;
        try
        {
            existingReminder = await _reminderCollection.Find(filter).FirstOrDefaultAsync().ConfigureAwait(false);
            if (existingReminder is not { IsActive: true })
            {
                _logger.LogWarning("Attempted to edit non-existent or inactive reminder (ID: {UserId}/{Seq}).", userId,
                    sequenceNumber);
                return (false, false, null); // Not found or inactive
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch reminder for editing (ID: {UserId}/{Seq}).", userId, sequenceNumber);
            return (false, true, null);
        }


        var updateDefinition = Builders<ReminderModel>.Update.Combine();
        var hasChanges = false;

        if (newMessage != null)
        {
            updateDefinition = updateDefinition.Set(r => r.Message, newMessage);
            hasChanges = true;
        }

        if (newTriggerTime.HasValue)
        {
            var utcTime = newTriggerTime.Value.ToUniversalTime();
            if (utcTime <= DateTime.UtcNow)
            {
                _logger.LogWarning("Attempted to set reminder time to the past for (ID: {UserId}/{Seq}).", userId,
                    sequenceNumber);
                return (false, true, null);
            }

            updateDefinition = updateDefinition.Set(r => r.TriggerTime, utcTime);
            updateDefinition = updateDefinition.Set(r => r.LastTriggered, null);
            hasChanges = true;
        }

        if (newRecurrence != null)
        {
            var recurrenceValue = newRecurrence.Equals("none", StringComparison.OrdinalIgnoreCase)
                ? null
                : newRecurrence.ToLowerInvariant();
            updateDefinition = updateDefinition.Set(r => r.Recurrence, recurrenceValue);
            updateDefinition =
                updateDefinition.Set(r => r.LastTriggered, null);
            hasChanges = true;
        }

        if (newTitle != null)
        {
            updateDefinition = updateDefinition.Set(r => r.Title, newTitle);
            hasChanges = true;
        }


        if (!hasChanges)
        {
            _logger.LogInformation("No changes specified for editing reminder (ID: {UserId}/{Seq}).", userId,
                sequenceNumber);
            return (true, true, existingReminder);
        }

        try
        {
            var options = new FindOneAndUpdateOptions<ReminderModel, ReminderModel>
            {
                ReturnDocument = ReturnDocument.After
            };
            var updatedDoc = await _reminderCollection.FindOneAndUpdateAsync(filter, updateDefinition, options)
                .ConfigureAwait(false);

            if (updatedDoc != null)
            {
                _logger.LogInformation("Successfully edited reminder (ID: {UserId}/{Seq}).", userId, sequenceNumber);
                return (true, true, updatedDoc);
            }

            _logger.LogWarning("Failed to find reminder during update phase (ID: {UserId}/{Seq}).", userId,
                sequenceNumber);
            return (false, false, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to edit reminder (ID: {UserId}/{Seq}).", userId, sequenceNumber);
            return (false, true, null);
        }
    }

    // --- Time Parsing Utility ---
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
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing time string '{timeString}': {ex.Message}");
            return null;
        }
    }
}