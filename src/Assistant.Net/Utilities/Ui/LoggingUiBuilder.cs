using Assistant.Net.Data.Entities;
using Assistant.Net.Data.Enums;
using Discord;

namespace Assistant.Net.Utilities.Ui;

public static class LoggingUiBuilder
{
    public const string IdConfigToggle = "log:cfg:toggle";
    public const string IdConfigChannel = "log:cfg:channel";
    public const string IdConfigDelay = "log:cfg:delay";

    public static MessageComponent BuildDashboard(List<LogSettingsEntity> configs)
    {
        var container = new ContainerBuilder();
        container.WithTextDisplay(new TextDisplayBuilder("# Logging Configuration"));
        container.WithSeparator();

        foreach (var config in configs)
        {
            var typeStr = config.LogType.ToString();
            var emoji = GetLogTypeEmoji(config.LogType);
            var statusEmoji = config.IsEnabled ? "✅" : "❌";
            var statusText = config.IsEnabled ? "Enabled" : "Disabled";

            container.WithTextDisplay(new TextDisplayBuilder($"### {emoji} {typeStr} Logging"));

            var delayText = config.DeleteDelayMs <= 0 ? "Permanent" : FormatDuration(config.DeleteDelayMs);
            var channelText = config.ChannelId.HasValue ? $"<#{config.ChannelId}>" : "Not Set";

            container.WithTextDisplay(new TextDisplayBuilder(
                $"**Status:** {statusEmoji} {statusText} | **Channel:** {channelText} | **Auto-Delete:** {delayText}"));

            var toggleStyle = config.IsEnabled ? ButtonStyle.Success : ButtonStyle.Secondary;
            var toggleLabel = config.IsEnabled ? "Enabled" : "Disabled";

            container.WithActionRow(new ActionRowBuilder()
                .WithButton(toggleLabel, $"{IdConfigToggle}:{typeStr}", toggleStyle)
                .WithButton("Set Delete Delay", $"{IdConfigDelay}:{typeStr}", ButtonStyle.Primary, new Emoji("⏲️")));

            var channelSelect = new SelectMenuBuilder()
                .WithCustomId($"{IdConfigChannel}:{typeStr}")
                .WithType(ComponentType.ChannelSelect)
                .WithChannelTypes(ChannelType.Text)
                .WithPlaceholder($"Select {typeStr} Log Channel...");

            container.WithActionRow(new ActionRowBuilder().WithComponents([channelSelect]));

            container.WithSeparator();
        }

        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    private static string GetLogTypeEmoji(LogType type) => type switch
    {
        LogType.Message => "💬",
        LogType.Voice => "🎙️",
        LogType.User => "👤",
        LogType.Presence => "🟢",
        _ => "❓"
    };

    private static string FormatDuration(int ms)
    {
        var span = TimeSpan.FromMilliseconds(ms);
        if (span.TotalDays >= 1) return $"{span.TotalDays:F1} days";
        if (span.TotalHours >= 1) return $"{span.TotalHours:F1} hours";
        if (span.TotalMinutes >= 1) return $"{span.TotalMinutes:F0} mins";
        return $"{span.TotalSeconds:F0} seconds";
    }
}