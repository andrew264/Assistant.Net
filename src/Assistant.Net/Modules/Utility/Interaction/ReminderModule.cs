using Assistant.Net.Services.Features;
using Assistant.Net.Utilities;
using Assistant.Net.Utilities.Ui;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;

namespace Assistant.Net.Modules.Utility.Interaction;

public class ReminderModule(ReminderService reminderService)
    : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("remind", "Set a reminder for yourself, another user, or a channel.")]
    public async Task RemindAsync(
        [Summary("target", "Who or what to remind (User or Channel). Leave empty for yourself.")]
        IMentionable? target = null)
    {
        var targetId = Context.User.Id;
        var isDm = true;
        var contextLabel = "Yourself";

        if (target != null)
            switch (target)
            {
                case IUser { IsBot: true }:
                    await RespondAsync("You cannot set reminders for bots.", ephemeral: true).ConfigureAwait(false);
                    return;
                case IUser user:
                {
                    if (user.Id != Context.User.Id)
                    {
                        var permissions = (Context.User as IGuildUser)?.GuildPermissions;
                        if (permissions is not { ManageMessages: true })
                        {
                            await RespondAsync(
                                "You need 'Manage Messages' permission to set reminders for other users.",
                                ephemeral: true).ConfigureAwait(false);
                            return;
                        }

                        targetId = user.Id;
                        contextLabel = $"@{user.Username}";
                    }

                    break;
                }
                case ITextChannel channel:
                    targetId = channel.Id;
                    isDm = false;
                    contextLabel = $"#{channel.Name}";
                    break;
                default:
                    await RespondAsync("Invalid target. Please select a User or a Text Channel.", ephemeral: true)
                        .ConfigureAwait(false);
                    return;
            }

        var typeStr = isDm ? "dm" : "channel";
        var customId = $"remind:create:modal:{targetId}:{typeStr}";

        var modal = new ModalBuilder()
            .WithTitle($"Set Reminder: {contextLabel}")
            .WithCustomId(customId)
            .AddTextInput("Title (Optional)", "reminder_title", maxLength: 200, required: false,
                placeholder: "e.g., Weekly Meeting")
            .AddTextInput("Message", "message", TextInputStyle.Paragraph, maxLength: 1000, required: true,
                placeholder: "What do you need to be reminded about?")
            .AddTextInput("When?", "time", placeholder: "e.g., in 5 mins, tomorrow 9am", required: true)
            .AddTextInput("Repeat (Optional)", "repeat", placeholder: "e.g., daily, weekly, none", required: false,
                maxLength: 50)
            .Build();

        await RespondWithModalAsync(modal).ConfigureAwait(false);
    }

    [ModalInteraction("remind:create:modal:*:*", true)]
    public async Task HandleCreateModalAsync(string targetIdStr, string typeStr, ReminderCreateModal modal)
    {
        await DeferAsync().ConfigureAwait(false);

        if (!ulong.TryParse(targetIdStr, out var targetId))
        {
            await FollowupAsync("Invalid target ID.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var isDm = typeStr == "dm";
        var channelId = Context.Channel.Id;
        var targetUserId = isDm ? targetId : Context.User.Id;

        if (!isDm) channelId = targetId;

        await CreateReminderLogicAsync(
            modal.Time,
            modal.Message,
            isDm,
            targetUserId,
            channelId,
            modal.Repeat,
            modal.ReminderTitle
        ).ConfigureAwait(false);
    }

    private async Task CreateReminderLogicAsync(
        string timeString,
        string message,
        bool isDm,
        ulong targetUserId,
        ulong channelId,
        string? recurrence = null,
        string? title = null)
    {
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

        if (!string.IsNullOrWhiteSpace(recurrence) && !IsValidRecurrence(recurrence))
        {
            await FollowupAsync(
                "Invalid recurrence interval. Use 'daily', 'weekly', 'monthly', 'yearly', 'hourly', 'minutely', 'every X unit', or 'none'.",
                ephemeral: true).ConfigureAwait(false);
            return;
        }

        var dTargetId = targetUserId == Context.User.Id && isDm ? null : (ulong?)targetUserId;

        var reminder = await reminderService.CreateReminderAsync(
            Context.User.Id,
            Context.Guild.Id,
            channelId,
            message,
            parsedTime.Value,
            isDm,
            dTargetId,
            recurrence,
            title).ConfigureAwait(false);

        if (reminder != null)
        {
            var targetDisplay = isDm
                ? targetUserId == Context.User.Id ? "you" : $"<@{targetUserId}>"
                : $"<#{channelId}>";

            var container = new ContainerBuilder()
                .WithTextDisplay(new TextDisplayBuilder("# ✅ Reminder Set!"))
                .WithTextDisplay(new TextDisplayBuilder(
                    $"I will remind {targetDisplay} {reminder.TriggerTime.GetRelativeTime()}."))
                .WithSeparator()
                .WithTextDisplay(new TextDisplayBuilder($"**Message:** \"{reminder.Message.Truncate(500)}\""))
                .WithTextDisplay(new TextDisplayBuilder($"**ID:** `{reminder.Id}`"))
                .WithTextDisplay(
                    new TextDisplayBuilder($"**Time:** {reminder.TriggerTime.GetLongDateTime()}"));

            if (!string.IsNullOrWhiteSpace(reminder.Title))
                container.WithTextDisplay(new TextDisplayBuilder($"**Title:** {reminder.Title}"));

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

    [SlashCommand("reminders", "Manage your reminders.")]
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

    [ComponentInteraction(ReminderUiBuilder.IdEditModal + ":*", true)]
    public async Task HandleEditModalPromptAsync(int reminderId)
    {
        var reminder = await reminderService.GetReminderAsync(Context.User.Id, reminderId).ConfigureAwait(false);
        if (reminder == null)
        {
            await RespondAsync("Reminder not found.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var modalBuilder = new ModalBuilder()
            .WithTitle($"Edit Reminder #{reminder.Id}")
            .WithCustomId($"remind:modal:edit:{reminder.Id}")
            .AddTextInput("Title (Optional)", "reminder_title", value: reminder.Title, required: false, maxLength: 200)
            .AddTextInput("Message", "message", TextInputStyle.Paragraph, value: reminder.Message, maxLength: 1000)
            .AddTextInput("Time (e.g., 'in 1 hour', 'tomorrow')", "time", placeholder: "Leave empty to keep current",
                required: false)
            .AddTextInput("Repeat (e.g., 'daily', 'none')", "repeat", value: reminder.Recurrence ?? "none",
                required: false, maxLength: 50);
        if (reminder.IsDm)
        {
            var menu = new SelectMenuBuilder()
                .WithCustomId("remind:edit:user")
                .WithType(ComponentType.UserSelect)
                .WithMinValues(1)
                .WithMaxValues(1);

            var currentTargetId = reminder.TargetUserId ?? reminder.CreatorId;
            menu.AddDefaultValue(currentTargetId, SelectDefaultValueType.User);
            modalBuilder.AddSelectMenu("Target User", menu);
        }
        else
        {
            var menu = new SelectMenuBuilder()
                .WithCustomId("remind:edit:chan")
                .WithType(ComponentType.ChannelSelect)
                .WithChannelTypes(ChannelType.Text)
                .WithMinValues(1)
                .WithMaxValues(1);

            if (reminder.ChannelId != 0)
                menu.AddDefaultValue(reminder.ChannelId, SelectDefaultValueType.Channel);
            modalBuilder.AddSelectMenu("Target Channel", menu);
        }

        await RespondWithModalAsync(modalBuilder.Build()).ConfigureAwait(false);
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

        ulong? newChannelId = null;
        ulong? newTargetUserId = null;

        if (Context.Interaction is SocketModal socketModal)
        {
            var userSelect = socketModal.Data.Components
                .FirstOrDefault(c => c.CustomId == "remind:edit:user");
            var chanSelect = socketModal.Data.Components
                .FirstOrDefault(c => c.CustomId == "remind:edit:chan");

            if (userSelect is { Values.Count: > 0 } && ulong.TryParse(userSelect.Values.First(), out var uid))
            {
                var user = await Context.Client.GetUserAsync(uid);
                if (user is { IsBot: true })
                {
                    await FollowupAsync("You cannot set a reminder for a bot.", ephemeral: true).ConfigureAwait(false);
                    return;
                }

                newTargetUserId = uid;
            }
            else if (chanSelect is { Values.Count: > 0 } && ulong.TryParse(chanSelect.Values.First(), out var cid))
            {
                newChannelId = cid;
            }
        }

        var (success, found, updatedReminder) = await reminderService.EditReminderAsync(
            Context.User.Id,
            reminderId,
            modal.Message,
            newParsedTime,
            modal.Repeat,
            modal.ReminderTitle,
            newChannelId,
            newTargetUserId
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
}

public class ReminderCreateModal : IModal
{
    [ModalTextInput("reminder_title")] public string? ReminderTitle { get; set; }
    [ModalTextInput("message")] public string Message { get; set; } = string.Empty;
    [ModalTextInput("time")] public string Time { get; set; } = string.Empty;
    [ModalTextInput("repeat")] public string? Repeat { get; set; }
    public string Title => "Set Reminder";
}

public class ReminderEditModal : IModal
{
    [ModalTextInput("reminder_title")] public string? ReminderTitle { get; set; }
    [ModalTextInput("message")] public string Message { get; set; } = string.Empty;
    [ModalTextInput("time")] public string? Time { get; set; }
    [ModalTextInput("repeat")] public string? Repeat { get; set; }
    public string Title => "Edit Reminder";
}