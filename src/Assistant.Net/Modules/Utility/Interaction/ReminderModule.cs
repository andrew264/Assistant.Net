using Assistant.Net.Services.Features;
using Assistant.Net.Utilities;
using Assistant.Net.Utilities.Ui;
using Discord;
using Discord.Interactions;

namespace Assistant.Net.Modules.Utility.Interaction;

[Group("remind", "Set, view, and manage reminders.")]
[RequireContext(ContextType.Guild)]
public class ReminderModule(ReminderService reminderService)
    : InteractionModuleBase<SocketInteractionContext>
{
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

    [SlashCommand("list", "Manage your reminders.")]
    public async Task ListRemindersAsync()
    {
        await DeferAsync(true).ConfigureAwait(false);
        var reminders = await reminderService.ListUserRemindersAsync(Context.User.Id).ConfigureAwait(false);

        var components = ReminderUiBuilder.BuildReminderList(reminders, Context.User, 1);
        await FollowupAsync(components: components, ephemeral: true, flags: MessageFlags.ComponentsV2)
            .ConfigureAwait(false);
    }


    [ComponentInteraction(ReminderUiBuilder.IdPage + ":*", true)]
    private async Task HandlePaginationAsync(int page)
    {
        await DeferAsync(true).ConfigureAwait(false);
        var reminders = await reminderService.ListUserRemindersAsync(Context.User.Id).ConfigureAwait(false);
        var components = ReminderUiBuilder.BuildReminderList(reminders, Context.User, page);

        await ModifyOriginalResponseAsync(msg =>
        {
            msg.Components = components;
            msg.Flags = MessageFlags.ComponentsV2;
        }).ConfigureAwait(false);
    }

    [ComponentInteraction(ReminderUiBuilder.IdList + ":*", true)]
    public async Task HandleBackToListAsync(int page)
    {
        await HandlePaginationAsync(page);
    }

    [ComponentInteraction(ReminderUiBuilder.IdManage + ":*", true)]
    public async Task HandleManageAsync(int reminderId)
    {
        await DeferAsync(true).ConfigureAwait(false);
        var reminder = await reminderService.GetReminderAsync(Context.User.Id, reminderId).ConfigureAwait(false);

        if (reminder == null)
        {
            await FollowupAsync("Reminder not found or you don't have permission to manage it.", ephemeral: true)
                .ConfigureAwait(false);
            return;
        }

        var components = ReminderUiBuilder.BuildManageReminder(reminder);
        await ModifyOriginalResponseAsync(msg =>
        {
            msg.Components = components;
            msg.Flags = MessageFlags.ComponentsV2;
        }).ConfigureAwait(false);
    }

    [ComponentInteraction(ReminderUiBuilder.IdDelete + ":*", true)]
    public async Task HandleDeleteAsync(int reminderId)
    {
        await DeferAsync(true).ConfigureAwait(false);
        var (success, found) =
            await reminderService.DeleteReminderAsync(Context.User.Id, reminderId).ConfigureAwait(false);

        if (!found || !success)
        {
            await FollowupAsync("Failed to delete the reminder. It may no longer exist.", ephemeral: true)
                .ConfigureAwait(false);
            return;
        }

        // Return to list after deletion
        var reminders = await reminderService.ListUserRemindersAsync(Context.User.Id).ConfigureAwait(false);
        var components = ReminderUiBuilder.BuildReminderList(reminders, Context.User, 1);

        await ModifyOriginalResponseAsync(msg =>
        {
            msg.Components = components;
            msg.Flags = MessageFlags.ComponentsV2;
        }).ConfigureAwait(false);
    }

    [ComponentInteraction(ReminderUiBuilder.IdToggle + ":*", true)]
    public async Task HandleToggleAsync(int reminderId)
    {
        await DeferAsync(true).ConfigureAwait(false);
        var reminder = await reminderService.GetReminderAsync(Context.User.Id, reminderId).ConfigureAwait(false);

        if (reminder == null)
        {
            await FollowupAsync("Reminder not found.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var (success, _, updatedReminder) = await reminderService.EditReminderAsync(
            Context.User.Id, reminderId, isActive: !reminder.IsActive).ConfigureAwait(false);

        if (!success || updatedReminder == null)
        {
            await FollowupAsync("Failed to update reminder status.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var components = ReminderUiBuilder.BuildManageReminder(updatedReminder);
        await ModifyOriginalResponseAsync(msg =>
        {
            msg.Components = components;
            msg.Flags = MessageFlags.ComponentsV2;
        }).ConfigureAwait(false);
    }

    [ComponentInteraction(ReminderUiBuilder.IdTargetUser + ":*", true)]
    public async Task HandleTargetUserSelectAsync(int reminderId, string[] selectedUsers)
    {
        await DeferAsync(true).ConfigureAwait(false);

        if (selectedUsers.Length == 0 || !ulong.TryParse(selectedUsers[0], out var targetUserId)) return;

        var user = await Context.Client.GetUserAsync(targetUserId);
        if (user is { IsBot: true })
        {
            await FollowupAsync("You cannot set a reminder for a bot.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var (success, _, updatedReminder) = await reminderService.EditReminderAsync(
            Context.User.Id, reminderId, newTargetUserId: targetUserId).ConfigureAwait(false);

        if (!success || updatedReminder == null)
        {
            await FollowupAsync("Failed to update target user.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var components = ReminderUiBuilder.BuildManageReminder(updatedReminder);
        await ModifyOriginalResponseAsync(msg =>
        {
            msg.Components = components;
            msg.Flags = MessageFlags.ComponentsV2;
        }).ConfigureAwait(false);
    }

    [ComponentInteraction(ReminderUiBuilder.IdTargetChannel + ":*", true)]
    public async Task HandleTargetChannelSelectAsync(int reminderId, string[] selectedChannels)
    {
        await DeferAsync(true).ConfigureAwait(false);

        if (selectedChannels.Length == 0 || !ulong.TryParse(selectedChannels[0], out var channelId)) return;

        var (success, _, updatedReminder) = await reminderService.EditReminderAsync(
            Context.User.Id, reminderId, newChannelId: channelId).ConfigureAwait(false);

        if (!success || updatedReminder == null)
        {
            await FollowupAsync("Failed to update target channel.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var components = ReminderUiBuilder.BuildManageReminder(updatedReminder);
        await ModifyOriginalResponseAsync(msg =>
        {
            msg.Components = components;
            msg.Flags = MessageFlags.ComponentsV2;
        }).ConfigureAwait(false);
    }

    [ComponentInteraction(ReminderUiBuilder.IdEditModal + ":*", true)]
    public async Task HandleEditModalPromptAsync(int reminderId)
    {
        var reminder = await reminderService.GetReminderAsync(Context.User.Id, reminderId).ConfigureAwait(false);
        if (reminder == null)
        {
            await RespondAsync("Reminder not found.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var modal = new ModalBuilder()
            .WithTitle($"Edit Reminder #{reminder.Id}")
            .WithCustomId($"remind:modal:edit:{reminder.Id}")
            .AddTextInput("Title (Optional)", "title", value: reminder.Title, required: false, maxLength: 200)
            .AddTextInput("Message", "message", TextInputStyle.Paragraph, value: reminder.Message, maxLength: 1000)
            .AddTextInput("Time (e.g., 'in 1 hour', 'tomorrow')", "time", placeholder: "Leave empty to keep current",
                required: false)
            .AddTextInput("Repeat (e.g., 'daily', 'none')", "repeat", value: reminder.Recurrence ?? "none",
                required: false, maxLength: 50)
            .Build();

        await RespondWithModalAsync(modal).ConfigureAwait(false);
    }

    [ModalInteraction("remind:modal:edit:*", true)]
    public async Task HandleEditModalSubmitAsync(int reminderId, ReminderEditModal modal)
    {
        await DeferAsync(true).ConfigureAwait(false);

        DateTime? newParsedTime = null;
        if (!string.IsNullOrWhiteSpace(modal.Time))
        {
            newParsedTime = ReminderService.ParseTime(modal.Time);
            if (newParsedTime == null)
            {
                await FollowupAsync("Invalid time format provided.", ephemeral: true)
                    .ConfigureAwait(false);
                return;
            }

            if (newParsedTime <= DateTime.UtcNow)
            {
                await FollowupAsync("New reminder time must be in the future.", ephemeral: true)
                    .ConfigureAwait(false);
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(modal.Repeat) && !IsValidRecurrence(modal.Repeat))
        {
            await FollowupAsync("Invalid recurrence interval provided.", ephemeral: true)
                .ConfigureAwait(false);
            return;
        }

        var (success, found, updatedReminder) = await reminderService.EditReminderAsync(
            Context.User.Id,
            reminderId,
            modal.Message,
            newParsedTime,
            modal.Repeat,
            modal.Title
        ).ConfigureAwait(false);

        if (!found)
        {
            await FollowupAsync("Reminder not found.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (!success || updatedReminder == null)
        {
            await FollowupAsync("Failed to apply edits due to an internal error.", ephemeral: true)
                .ConfigureAwait(false);
            return;
        }

        var components = ReminderUiBuilder.BuildManageReminder(updatedReminder);
        await ModifyOriginalResponseAsync(msg =>
        {
            msg.Components = components;
            msg.Flags = MessageFlags.ComponentsV2;
        }).ConfigureAwait(false);
    }
}

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

public class ReminderEditModal : IModal
{
    [ModalTextInput("title")] public string? TitleInput { get; set; }

    [ModalTextInput("message")] public string Message { get; set; } = string.Empty;

    [ModalTextInput("time")] public string? Time { get; set; }

    [ModalTextInput("repeat")] public string? Repeat { get; set; }

    public string Title => "Edit Reminder";
}