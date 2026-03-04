using Assistant.Net.Data.Entities;
using Discord;

namespace Assistant.Net.Utilities.Ui;

public static class ReminderUiBuilder
{
    private const int ItemsPerPage = 5;

    public const string IdManage = "remind:manage";
    public const string IdPage = "remind:page";
    public const string IdList = "remind:list";
    public const string IdEditModal = "remind:edit_modal";
    public const string IdToggle = "remind:toggle";
    public const string IdDelete = "remind:delete";

    public static MessageComponent BuildReminderList(List<ReminderEntity> reminders, IUser user, int currentPage)
    {
        var totalReminders = reminders.Count;
        var totalPages = Math.Max(1, (int)Math.Ceiling((double)totalReminders / ItemsPerPage));
        currentPage = Math.Clamp(currentPage, 1, totalPages);

        var container = new ContainerBuilder()
            .WithSection(s => s
                .AddComponent(new TextDisplayBuilder("# Your Reminders"))
                .AddComponent(new TextDisplayBuilder($"Found {totalReminders} reminder(s)."))
                .WithAccessory(new ThumbnailBuilder
                {
                    Media = new UnfurledMediaItemProperties
                        { Url = user.GetDisplayAvatarUrl() ?? user.GetDefaultAvatarUrl() }
                })
            )
            .WithSeparator();

        if (totalReminders == 0)
        {
            container.WithTextDisplay(new TextDisplayBuilder("You don't have any reminders set."));
            return new ComponentBuilderV2().WithContainer(container).Build();
        }

        var items = reminders.Skip((currentPage - 1) * ItemsPerPage).Take(ItemsPerPage).ToList();

        foreach (var r in items)
        {
            var title = string.IsNullOrWhiteSpace(r.Title) ? $"Reminder `{r.Id}`" : $"**{r.Title}** (`{r.Id}`)";
            var status = r.IsActive ? "Active" : "Paused";
            var time = r.TriggerTime > DateTime.UtcNow ? r.TriggerTime.GetRelativeTime() : "Past Due / Pending";
            var msg = r.Message.Truncate(60);

            var section = new SectionBuilder()
                .AddComponent(new TextDisplayBuilder($"{title} | {status}"))
                .AddComponent(new TextDisplayBuilder($"> {msg}"))
                .AddComponent(new TextDisplayBuilder($"⏰ {time}"))
                .WithAccessory(new ButtonBuilder("Manage", $"{IdManage}:{r.Id}", ButtonStyle.Secondary));

            container.WithSection(section);
            container.WithSeparator();
        }

        container.WithTextDisplay(new TextDisplayBuilder($"Page {currentPage} of {totalPages}"));

        if (totalPages > 1)
            container.WithActionRow(new ActionRowBuilder()
                .WithButton("◀ Prev", $"{IdPage}:{currentPage - 1}", ButtonStyle.Secondary,
                    disabled: currentPage <= 1)
                .WithButton("Next ▶", $"{IdPage}:{currentPage + 1}", ButtonStyle.Secondary,
                    disabled: currentPage >= totalPages)
            );

        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    public static MessageComponent BuildManageReminder(ReminderEntity reminder)
    {
        var container = new ContainerBuilder();
        var statusText = reminder.IsActive ? "Active" : "Paused";

        container.WithTextDisplay(new TextDisplayBuilder($"# Manage Reminder `{reminder.Id}`"));
        container.WithTextDisplay(new TextDisplayBuilder($"**Status:** {statusText}"));
        container.WithSeparator();

        var title = string.IsNullOrWhiteSpace(reminder.Title) ? "*(No Title)*" : reminder.Title;
        var target = reminder.IsDm
            ? reminder.TargetUserId == reminder.CreatorId ? "DM to Self" : $"DM to <@{reminder.TargetUserId}>"
            : $"Channel <#{reminder.ChannelId}>";

        var repeats = reminder.Recurrence ?? "None";
        var nextTrigger = reminder.TriggerTime > DateTime.UtcNow
            ? $"{reminder.TriggerTime.GetLongDateTime()} ({reminder.TriggerTime.GetRelativeTime()})"
            : $"{reminder.TriggerTime.GetLongDateTime()} (Overdue/Pending)";

        container.WithTextDisplay(new TextDisplayBuilder($"**Title:** {title}"));
        container.WithTextDisplay(new TextDisplayBuilder($"**Message:**\n> {reminder.Message}"));
        container.WithTextDisplay(new TextDisplayBuilder($"**Next Trigger:** {nextTrigger}"));
        container.WithTextDisplay(new TextDisplayBuilder($"**Repeats:** {repeats}"));
        container.WithTextDisplay(new TextDisplayBuilder($"**Target:** {target}"));
        container.WithSeparator();

        var toggleLabel = reminder.IsActive ? "Pause" : "Resume";
        var toggleStyle = reminder.IsActive ? ButtonStyle.Secondary : ButtonStyle.Success;

        container.WithActionRow(new ActionRowBuilder()
            .WithButton("Edit Details", $"{IdEditModal}:{reminder.Id}", ButtonStyle.Primary, new Emoji("📝"))
            .WithButton(toggleLabel, $"{IdToggle}:{reminder.Id}", toggleStyle,
                new Emoji(reminder.IsActive ? "⏸️" : "▶️"))
            .WithButton("Delete", $"{IdDelete}:{reminder.Id}", ButtonStyle.Danger, new Emoji("🗑️"))
        );

        container.WithActionRow(new ActionRowBuilder()
            .WithButton("Back to List", $"{IdList}:1", ButtonStyle.Secondary, new Emoji("◀️"))
        );

        return new ComponentBuilderV2().WithContainer(container).Build();
    }
}