using Assistant.Net.Services.GuildFeatures;
using Assistant.Net.Utilities;
using Discord;
using Discord.Interactions;

namespace Assistant.Net.Modules.Reminder.InteractionModules;

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
        await DeferAsync().ConfigureAwait(false);

        var parsedTime = ReminderService.ParseTime(timeString);

        if (parsedTime == null)
        {
            await FollowupAsync(
                "Invalid time format. Please use a recognizable format (e.g., 'in 5 minutes', 'at 2:30 PM', 'tomorrow 9am').",
                ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (parsedTime.Value <= DateTime.UtcNow)
        {
            await FollowupAsync("Reminder time must be in the future.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (message.Length > 1000)
        {
            await FollowupAsync("Reminder message is too long (max 1000 characters).", ephemeral: true)
                .ConfigureAwait(false);
            return;
        }

        if (recurrence != null && !IsValidRecurrence(recurrence))
        {
            await FollowupAsync(
                "Invalid recurrence interval. Use 'daily', 'weekly', 'monthly', 'yearly', 'hourly', 'minutely', 'every X unit', or 'none'.",
                ephemeral: true).ConfigureAwait(false);
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
            title).ConfigureAwait(false);

        if (reminder != null)
        {
            var targetString =
                isDm ? actualTargetUser.Id == Context.User.Id ? "you" : actualTargetUser.Mention : "this channel";

            var container = new ContainerBuilder()
                .WithTextDisplay(new TextDisplayBuilder("# ✅ Reminder Set!"))
                .WithTextDisplay(new TextDisplayBuilder(
                    $"I will remind {targetString} {reminder.TriggerTime.GetRelativeTime()}."))
                .WithSeparator()
                .WithTextDisplay(new TextDisplayBuilder($"**Message:** \"{reminder.Message.Truncate(500)}\""))
                .WithTextDisplay(new TextDisplayBuilder($"**ID:** `{reminder.Id}`"))
                .WithTextDisplay(
                    new TextDisplayBuilder($"**Time:** {reminder.TriggerTime.GetLongDateTime()}"));

            if (reminder.Recurrence != null)
                container.WithTextDisplay(new TextDisplayBuilder($"**Repeats:** {reminder.Recurrence}"));

            var components = new ComponentBuilderV2().WithContainer(container).Build();

            await FollowupAsync(components: components, flags: MessageFlags.ComponentsV2,
                allowedMentions: AllowedMentions.None).ConfigureAwait(false);
        }
        else
        {
            await FollowupAsync("Failed to create the reminder due to an internal error.", ephemeral: true)
                .ConfigureAwait(false);
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
        await CreateReminderInteractionAsync(time, message, true, Context.User, repeat, title).ConfigureAwait(false);
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
        await CreateReminderInteractionAsync(time, message, false, Context.User, repeat, title).ConfigureAwait(false);
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
            await RespondAsync("You cannot set reminders for bots.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await CreateReminderInteractionAsync(time, message, true, user, repeat, title).ConfigureAwait(false);
    }

    [SlashCommand("list", "Lists your upcoming reminders.")]
    public async Task ListRemindersAsync()
    {
        await DeferAsync(true).ConfigureAwait(false);
        var reminders = await reminderService.ListUserRemindersAsync(Context.User.Id).ConfigureAwait(false);

        if (reminders.Count == 0)
        {
            await FollowupAsync("You have no pending reminders.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var container = new ContainerBuilder()
            .WithSection(section =>
            {
                section.AddComponent(new TextDisplayBuilder($"# 🗓️ Your Pending Reminders ({reminders.Count})"));
                section.WithAccessory(new ThumbnailBuilder
                {
                    Media = new UnfurledMediaItemProperties
                        { Url = Context.User.GetDisplayAvatarUrl() ?? Context.User.GetDefaultAvatarUrl() }
                });
            });

        var count = 0;
        foreach (var reminder in reminders.OrderBy(r => r.TriggerTime))
        {
            if (count >= 25)
            {
                container.WithSeparator();
                container.WithTextDisplay(new TextDisplayBuilder("*Showing first 25 reminders.*"));
                break;
            }

            container.WithSeparator();
            var titleText = string.IsNullOrWhiteSpace(reminder.Title)
                ? $"**ID: `{reminder.Id}`**"
                : $"**ID: `{reminder.Id}`** - {reminder.Title.Truncate(100)}";

            var targetStr = reminder.IsDm
                ? reminder.TargetUserId == reminder.CreatorId ? "DM to Self" : $"DM to <@{reminder.TargetUserId}>"
                : $"in <#{reminder.ChannelId}>";
            var recurrenceStr = reminder.Recurrence != null ? $" (Repeats {reminder.Recurrence})" : "";

            container.WithTextDisplay(new TextDisplayBuilder(titleText));
            container.WithTextDisplay(new TextDisplayBuilder($"> {reminder.Message.Truncate(50)}"));
            container.WithTextDisplay(new TextDisplayBuilder(
                $"Triggers {reminder.TriggerTime.GetRelativeTime()} in {targetStr}{recurrenceStr}"));
            count++;
        }

        var components = new ComponentBuilderV2().WithContainer(container).Build();
        await FollowupAsync(components: components, ephemeral: true, flags: MessageFlags.ComponentsV2)
            .ConfigureAwait(false);
    }

    [SlashCommand("cancel", "Cancels or deletes one of your reminders.")]
    public async Task CancelReminderAsync(
        [Summary(description: "The ID number of the reminder to cancel (from /remind list).")]
        int id,
        [Summary(description: "Permanently delete the reminder instead of just deactivating it.")]
        bool delete = false)
    {
        await DeferAsync(true).ConfigureAwait(false);

        var (success, found) =
            await reminderService.CancelReminderAsync(Context.User.Id, id, delete).ConfigureAwait(false);

        if (found)
        {
            if (success)
                await FollowupAsync($"Reminder ID `{id}` {(delete ? "deleted" : "deactivated")}.", ephemeral: true)
                    .ConfigureAwait(false);
            else
                await FollowupAsync(
                    $"Failed to {(delete ? "delete" : "deactivate")} reminder ID `{id}`. An internal error occurred.",
                    ephemeral: true).ConfigureAwait(false);
        }
        else
        {
            await FollowupAsync($"Could not find an active reminder with ID `{id}` belonging to you.", ephemeral: true)
                .ConfigureAwait(false);
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
                ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync(true).ConfigureAwait(false);

        DateTime? newParsedTime = null;
        if (time != null)
        {
            newParsedTime = ReminderService.ParseTime(time);
            if (newParsedTime == null)
            {
                await FollowupAsync("Invalid new time format provided.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            if (newParsedTime <= DateTime.UtcNow)
            {
                await FollowupAsync("New reminder time must be in the future.", ephemeral: true).ConfigureAwait(false);
                return;
            }
        }

        if (message?.Length > 1000)
        {
            await FollowupAsync("New reminder message is too long (max 1000 characters).", ephemeral: true)
                .ConfigureAwait(false);
            return;
        }

        if (repeat != null && !IsValidRecurrence(repeat))
        {
            await FollowupAsync("Invalid recurrence interval provided.", ephemeral: true).ConfigureAwait(false);
            return;
        }


        var (success, found, updatedReminder) = await reminderService.EditReminderAsync(
            Context.User.Id,
            id,
            message,
            newParsedTime,
            repeat,
            title
        ).ConfigureAwait(false);

        if (found)
        {
            if (success && updatedReminder != null)
            {
                var container = new ContainerBuilder()
                    .WithTextDisplay(new TextDisplayBuilder($"# ✅ Reminder ID `{id}` Updated"));

                var recurrenceStr = updatedReminder.Recurrence != null
                    ? $" (Repeats {updatedReminder.Recurrence})"
                    : "";
                var targetStr = updatedReminder.IsDm
                    ? updatedReminder.TargetUserId == updatedReminder.CreatorId
                        ? "DM to Self"
                        : $"DM to <@{updatedReminder.TargetUserId}>"
                    : $"in <#{updatedReminder.ChannelId}>";

                container.WithSeparator()
                    .WithTextDisplay(new TextDisplayBuilder(
                        $"**New Trigger Time:** {updatedReminder.TriggerTime.GetLongDateTime()} ({updatedReminder.TriggerTime.GetRelativeTime()}){recurrenceStr}"))
                    .WithTextDisplay(new TextDisplayBuilder($"**Message:** {updatedReminder.Message}"))
                    .WithTextDisplay(new TextDisplayBuilder($"**Target:** {targetStr}"));

                if (!string.IsNullOrWhiteSpace(updatedReminder.Title))
                    container.WithTextDisplay(new TextDisplayBuilder($"**Title:** {updatedReminder.Title}"));


                var components = new ComponentBuilderV2().WithContainer(container).Build();
                await FollowupAsync(components: components, ephemeral: true, flags: MessageFlags.ComponentsV2)
                    .ConfigureAwait(false);
            }
            else
            {
                await FollowupAsync(
                    $"Failed to edit reminder ID `{id}`. An internal error occurred or input was invalid (e.g., time in the past).",
                    ephemeral: true).ConfigureAwait(false);
            }
        }
        else
        {
            await FollowupAsync($"Could not find an active reminder with ID `{id}` belonging to you.", ephemeral: true)
                .ConfigureAwait(false);
        }
    }

    [SlashCommand("help", "Shows help information for reminder commands.")]
    public async Task RemindHelpAsync()
    {
        var container = new ContainerBuilder()
            .WithTextDisplay(new TextDisplayBuilder("# ⏰ Remind Help"))
            .WithTextDisplay(
                new TextDisplayBuilder("`/remind` commands allow you to set reminders for yourself or others."))
            .WithSeparator()
            .WithTextDisplay(new TextDisplayBuilder("### Commands"))
            .WithTextDisplay(new TextDisplayBuilder(
                "**`/remind me <time> <message> [title] [repeat]`**\nSets a reminder sent via DM."))
            .WithTextDisplay(new TextDisplayBuilder(
                "**`/remind channel <time> <message> [title] [repeat]`**\nSets a reminder posted in the current channel."))
            .WithTextDisplay(new TextDisplayBuilder(
                "**`/remind other <user> <time> <message> [title] [repeat]`**\nSets a reminder for another user via DM (Requires `Manage Messages` permission)."))
            .WithTextDisplay(new TextDisplayBuilder(
                "**`/remind list`**\nLists your upcoming reminders."))
            .WithTextDisplay(new TextDisplayBuilder(
                "**`/remind cancel <id> [delete]`**\nDeactivates (or permanently deletes if `delete:True`) a reminder by its ID number."))
            .WithTextDisplay(new TextDisplayBuilder(
                "**`/remind edit <id> [message] [time] [repeat] [title]`**\nEdits an existing reminder."))
            .WithSeparator()
            .WithTextDisplay(new TextDisplayBuilder("### Time Format Examples"))
            .WithTextDisplay(new TextDisplayBuilder(
                "`in 5 minutes`, `1 hour`, `tomorrow at 9am`, `next friday 5pm`, `25 dec 10:00`"))
            .WithSeparator()
            .WithTextDisplay(new TextDisplayBuilder("### Repeat Format Examples"))
            .WithTextDisplay(new TextDisplayBuilder(
                "`none`, `daily`, `weekly`, `monthly`, `yearly`, `hourly`, `minutely`, `every 2 days`, `every 3 weeks`"));

        var components = new ComponentBuilderV2().WithContainer(container).Build();
        await RespondAsync(components: components, ephemeral: true, flags: MessageFlags.ComponentsV2)
            .ConfigureAwait(false);
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