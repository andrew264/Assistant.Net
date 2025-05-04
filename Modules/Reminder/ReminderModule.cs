using Assistant.Net.Services;
using Assistant.Net.Utilities;
using Discord;
using Discord.Interactions;

namespace Assistant.Net.Modules.Reminder;

[Group("remind", "Set, view, and manage reminders.")]
[RequireContext(ContextType.Guild)]
public class ReminderModule(ReminderService reminderService)
    : InteractionModuleBase<SocketInteractionContext>
{
    // --- Helper to create reminders ---
    private async Task CreateReminderInteractionAsync(
        string timeString,
        string message,
        bool isDm = true,
        IUser? targetUser = null,
        string? recurrence = null,
        string? title = null)
    {
        await DeferAsync(true);

        var parsedTime = ReminderService.ParseTime(timeString);

        if (parsedTime == null)
        {
            await FollowupAsync(
                "Invalid time format. Please use a recognizable format (e.g., 'in 5 minutes', 'at 2:30 PM', 'tomorrow 9am').",
                ephemeral: true);
            return;
        }

        if (parsedTime.Value <= DateTime.UtcNow)
        {
            await FollowupAsync("Reminder time must be in the future.", ephemeral: true);
            return;
        }

        if (message.Length > 1000)
        {
            await FollowupAsync("Reminder message is too long (max 1000 characters).", ephemeral: true);
            return;
        }

        if (recurrence != null && !IsValidRecurrence(recurrence))
        {
            await FollowupAsync(
                "Invalid recurrence interval. Use 'daily', 'weekly', 'monthly', 'yearly', 'hourly', 'minutely', 'every X unit', or 'none'.",
                ephemeral: true);
            return;
        }

        var actualTargetUser = targetUser ?? Context.User;

        var reminder = await reminderService.CreateReminderAsync(
            Context.User.Id,
            Context.Guild.Id,
            Context.Channel.Id,
            message,
            parsedTime.Value,
            isDm,
            actualTargetUser.Id,
            recurrence,
            title);

        if (reminder != null)
        {
            var targetString =
                isDm ? actualTargetUser.Id == Context.User.Id ? "you" : actualTargetUser.Mention : "here";
            var timeUntil = ReminderService.GetRelativeTimeString(reminder.TriggerTime);
            await FollowupAsync(
                $"Okay {Context.User.Mention}, I'll remind {targetString} {timeUntil}: \"{reminder.Message}\" (ID: `{reminder.Id.SequenceNumber}`)",
                ephemeral: false, allowedMentions: AllowedMentions.None);
        }
        else
        {
            await FollowupAsync("Failed to create the reminder due to an internal error.", ephemeral: true);
        }
    }

    private static bool IsValidRecurrence(string recurrence)
    {
        recurrence = recurrence.ToLowerInvariant();
        if (recurrence == "none") return true;
        string[] validUnits = ["daily", "weekly", "monthly", "yearly", "hourly", "minutely"];
        if (validUnits.Contains(recurrence)) return true;
        if (!recurrence.StartsWith("every")) return false;
        var parts = recurrence.Split([' '], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 || !int.TryParse(parts[1], out var count) || count <= 0) return false;
        string[] validEveryUnits =
        [
            "day", "days", "week", "weeks", "month", "months", "year", "years", "hour", "hours", "minute",
            "minutes"
        ];
        return validEveryUnits.Contains(parts[2]);
    }


    // --- Commands ---
    [SlashCommand("me", "Sets a reminder for yourself (DM).")]
    public async Task RemindMeAsync(
        [Summary(description: "When to remind (e.g., 'in 5 minutes', 'tomorrow at 9am')")]
        string time,
        [Summary(description: "What to remind you about")]
        string message,
        [Summary(description: "Optional title for the reminder")]
        string? title = null,
        [Summary(description: "How often to repeat (e.g., 'daily', 'weekly', 'every 2 days', 'none')")]
        [Autocomplete(typeof(RecurrenceAutocompleteProvider))]
        string? repeat = null
    )
    {
        await CreateReminderInteractionAsync(time, message, true, Context.User, repeat, title);
    }

    [SlashCommand("channel", "Sets a reminder in this channel.")]
    public async Task RemindChannelAsync(
        [Summary(description: "When to remind (e.g., 'in 5 minutes', 'tomorrow at 9am')")]
        string time,
        [Summary(description: "What to remind about")]
        string message,
        [Summary(description: "Optional title for the reminder")]
        string? title = null,
        [Summary(description: "How often to repeat (e.g., 'daily', 'weekly', 'every 2 days', 'none')")]
        [Autocomplete(typeof(RecurrenceAutocompleteProvider))]
        string? repeat = null)
    {
        await CreateReminderInteractionAsync(time, message, false, Context.User, repeat, title);
    }

    [SlashCommand("other", "Sets a reminder for another user (DM).")]
    [RequireUserPermission(GuildPermission.ManageMessages)]
    public async Task RemindOtherAsync(
        [Summary(description: "The user to remind")]
        IUser user,
        [Summary(description: "When to remind (e.g., 'in 5 minutes', 'tomorrow at 9am')")]
        string time,
        [Summary(description: "What to remind them about")]
        string message,
        [Summary(description: "Optional title for the reminder")]
        string? title = null,
        [Summary(description: "How often to repeat (e.g., 'daily', 'weekly', 'every 2 days', 'none')")]
        [Autocomplete(typeof(RecurrenceAutocompleteProvider))]
        string? repeat = null)
    {
        if (user.IsBot)
        {
            await RespondAsync("You cannot set reminders for bots.", ephemeral: true);
            return;
        }

        await CreateReminderInteractionAsync(time, message, true, user, repeat, title);
    }

    [SlashCommand("list", "Lists your upcoming reminders.")]
    public async Task ListRemindersAsync()
    {
        await DeferAsync(true);
        var reminders = await reminderService.ListUserRemindersAsync(Context.User.Id);

        if (reminders.Count == 0)
        {
            await FollowupAsync("You have no pending reminders.", ephemeral: true);
            return;
        }

        var embed = new EmbedBuilder()
            .WithTitle($"🗓️ Your Pending Reminders ({reminders.Count})")
            .WithColor(Color.Green)
            .WithTimestamp(DateTimeOffset.UtcNow);

        var count = 0;
        foreach (var reminder in reminders.OrderBy(r => r.TriggerTime))
        {
            if (count >= 25) 
            {
                embed.WithFooter("Showing first 25 reminders.");
                break;
            }

            var title = string.IsNullOrWhiteSpace(reminder.Title)
                ? $"ID: {reminder.Id.SequenceNumber}"
                : $"ID: {reminder.Id.SequenceNumber} - {reminder.Title}";
            var recurrenceStr = reminder.Recurrence != null ? $" (Repeats {reminder.Recurrence})" : "";
            var targetStr = reminder.IsDm
                ? reminder.TargetUserId == reminder.Id.UserId ? " (DM to Self)" : $" (DM to <@{reminder.TargetUserId}>)"
                : $" (in <#{reminder.ChannelId}>)";

            embed.AddField(
                title.Length > 256 ? title[..253] + "..." : title,
                $"Time: {TimestampTag.FromDateTime(reminder.TriggerTime, TimestampTagStyles.Relative)}{recurrenceStr}{targetStr}\nMessage: {reminder.Message[..Math.Min(reminder.Message.Length, 150)]}{(reminder.Message.Length > 150 ? "..." : "")}");
            count++;
        }

        await FollowupAsync(embed: embed.Build(), ephemeral: true);
    }

    [SlashCommand("cancel", "Cancels or deletes one of your reminders.")]
    public async Task CancelReminderAsync(
        [Summary(description: "The ID number of the reminder to cancel (from /remind list).")]
        int id,
        [Summary(description: "Permanently delete the reminder instead of just deactivating it.")]
        bool delete = false)
    {
        await DeferAsync(true);

        var (success, found) = await reminderService.CancelReminderAsync(Context.User.Id, id, delete);

        if (found)
        {
            if (success)
                await FollowupAsync($"Reminder ID `{id}` {(delete ? "deleted" : "deactivated")}.", ephemeral: true);
            else
                await FollowupAsync(
                    $"Failed to {(delete ? "delete" : "deactivate")} reminder ID `{id}`. An internal error occurred.",
                    ephemeral: true);
        }
        else
        {
            await FollowupAsync($"Could not find an active reminder with ID `{id}` belonging to you.", ephemeral: true);
        }
    }

    [SlashCommand("edit", "Edits an existing reminder.")]
    public async Task EditReminderAsync(
        [Summary(description: "The ID number of the reminder to edit (from /remind list).")]
        int id,
        [Summary(description: "The new message for the reminder.")]
        string? message = null,
        [Summary(description: "The new time for the reminder (e.g., 'in 1 hour', 'next friday 5pm').")]
        string? time = null,
        [Summary(description: "The new recurrence interval (e.g., 'daily', 'weekly', 'none').")]
        [Autocomplete(typeof(RecurrenceAutocompleteProvider))]
        string? repeat = null,
        [Summary(description: "The new title for the reminder.")]
        string? title = null
    )
    {
        if (message == null && time == null && repeat == null && title == null)
        {
            await RespondAsync("You must provide at least one field to edit (message, time, repeat, or title).",
                ephemeral: true);
            return;
        }

        await DeferAsync(true);

        DateTime? newParsedTime = null;
        if (time != null)
        {
            newParsedTime = ReminderService.ParseTime(time);
            if (newParsedTime == null)
            {
                await FollowupAsync("Invalid new time format provided.", ephemeral: true);
                return;
            }

            if (newParsedTime <= DateTime.UtcNow)
            {
                await FollowupAsync("New reminder time must be in the future.", ephemeral: true);
                return;
            }
        }

        if (message?.Length > 1000)
        {
            await FollowupAsync("New reminder message is too long (max 1000 characters).", ephemeral: true);
            return;
        }

        if (repeat != null && !IsValidRecurrence(repeat))
        {
            await FollowupAsync("Invalid recurrence interval provided.", ephemeral: true);
            return;
        }


        var (success, found, updatedReminder) = await reminderService.EditReminderAsync(
            Context.User.Id,
            id,
            message,
            newParsedTime,
            repeat,
            title
        );

        if (found)
        {
            if (success)
            {
                if (updatedReminder != null)
                {
                    var embed = new EmbedBuilder()
                        .WithTitle($"✅ Reminder ID `{id}` Updated")
                        .WithColor(Color.Orange);

                    var recurrenceStr = updatedReminder.Recurrence != null
                        ? $" (Repeats {updatedReminder.Recurrence})"
                        : "";
                    var targetStr = updatedReminder.IsDm
                        ? updatedReminder.TargetUserId == updatedReminder.Id.UserId
                            ? " (DM to Self)"
                            : $" (DM to <@{updatedReminder.TargetUserId}>)"
                        : $" (in <#{updatedReminder.ChannelId}>)";

                    embed.AddField("New Trigger Time",
                        $"{TimestampTag.FromDateTime(updatedReminder.TriggerTime, TimestampTagStyles.LongDateTime)} ({TimestampTag.FromDateTime(updatedReminder.TriggerTime, TimestampTagStyles.Relative)}){recurrenceStr}");
                    embed.AddField("Message", updatedReminder.Message);
                    if (!string.IsNullOrWhiteSpace(updatedReminder.Title))
                        embed.AddField("Title", updatedReminder.Title);
                    embed.AddField("Target", targetStr);


                    await FollowupAsync(embed: embed.Build(), ephemeral: true);
                }
                else
                {
                    // Should not happen if success is true, but fallback
                    await FollowupAsync($"Reminder ID `{id}` updated successfully.", ephemeral: true);
                }
            }
            else
            {
                await FollowupAsync(
                    $"Failed to edit reminder ID `{id}`. An internal error occurred or input was invalid (e.g., time in the past).",
                    ephemeral: true);
            }
        }
        else
        {
            await FollowupAsync($"Could not find an active reminder with ID `{id}` belonging to you.", ephemeral: true);
        }
    }

    [SlashCommand("help", "Shows help information for reminder commands.")]
    public async Task RemindHelpAsync()
    {
        var embed = new EmbedBuilder()
            .WithTitle("⏰ Remind Help")
            .WithDescription("`/remind` commands allow you to set reminders for yourself or others.")
            .WithColor(Color.Blue)
            .AddField("`/remind me <time> <message> [title] [repeat]`", "Sets a reminder sent via DM.")
            .AddField("`/remind channel <time> <message> [title] [repeat]`",
                "Sets a reminder posted in the current channel.")
            .AddField("`/remind other <user> <time> <message> [title] [repeat]`",
                "Sets a reminder for another user via DM (Requires `Manage Messages` permission).")
            .AddField("`/remind list`", "Lists your upcoming reminders.")
            .AddField("`/remind cancel <id> [delete]`",
                "Deactivates (or permanently deletes if `delete:True`) a reminder by its ID number.")
            .AddField("`/remind edit <id> [message] [time] [repeat] [title]`", "Edits an existing reminder.")
            .AddField("Time Format Examples",
                "`in 5 minutes`, `1 hour`, `tomorrow at 9am`, `next friday 5pm`, `25 dec 10:00`")
            .AddField("Repeat Format Examples",
                "`none`, `daily`, `weekly`, `monthly`, `yearly`, `hourly`, `minutely`, `every 2 days`, `every 3 weeks`");

        await RespondAsync(embed: embed.Build(), ephemeral: true);
    }
}

// --- Autocomplete Providers ---

public class RecurrenceAutocompleteProvider : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        string[] suggestions =
        [
            "none", "daily", "weekly", "monthly", "yearly", "hourly", "minutely",
            "every 2 days", "every 3 weeks", "every 6 months"
        ];

        var currentValue = autocompleteInteraction.Data.Current.Value as string ?? "";

        var results = suggestions
            .Where(s => s.StartsWith(currentValue, StringComparison.OrdinalIgnoreCase))
            .Select(s => new AutocompleteResult(s.CapitalizeFirstLetter(), s))
            .Take(25);

        return Task.FromResult(AutocompletionResult.FromSuccess(results));
    }
}

public class ReminderTimeAutocompleteProvider : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        // Simple suggestions - more advanced parsing could be done here
        string[] suggestions =
        [
            "in 5 minutes", "in 15 minutes", "in 30 minutes", "in 1 hour", "in 2 hours",
            "tomorrow at 9am", "tomorrow noon", "next monday 10am", "in 1 week"
        ];

        var currentValue = autocompleteInteraction.Data.Current.Value as string ?? "";

        var results = suggestions
            .Where(s => s.StartsWith(currentValue, StringComparison.OrdinalIgnoreCase))
            .Select(s => new AutocompleteResult(s, s))
            .Take(25);

        return Task.FromResult(AutocompletionResult.FromSuccess(results));
    }
}