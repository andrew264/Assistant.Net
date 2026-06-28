using Assistant.Net.Data.Entities;
using Assistant.Net.Data.Enums;
using Discord;

namespace Assistant.Net.Utilities.Ui;

public static class LoggingUiBuilder
{
    public const string IdConfigOpen = "log:cfg:open";

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

            var delayText = config.DeleteDelayMs <= 0 ? "Permanent" : FormatDuration(config.DeleteDelayMs);
            var channelText = config.ChannelId.HasValue ? $"<#{config.ChannelId}>" : "Not Set";

            var section = new SectionBuilder()
                .AddComponent(new TextDisplayBuilder($"### {emoji} {typeStr} Logging"))
                .AddComponent(new TextDisplayBuilder(
                    $"**Status:** {statusEmoji} {statusText} | **Channel:** {channelText} | **Auto-Delete:** {delayText}"))
                .WithAccessory(new ButtonBuilder(customId: $"{IdConfigOpen}:{typeStr}", emote: new Emoji("⚙️")));

            container.WithSection(section);
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