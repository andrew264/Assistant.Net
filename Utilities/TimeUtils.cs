using Discord;

namespace Assistant.Net.Utilities;

public static class TimeUtils
{
    public static string GetLongDateTime(DateTimeOffset time) =>
        $"{new TimestampTag(time, TimestampTagStyles.LongDateTime)}";

    public static string GetRelativeTime(DateTimeOffset time) =>
        $"{new TimestampTag(time, TimestampTagStyles.Relative)}";

    public static TimeSpan ParseTimestamp(string timestamp)
    {
        if (string.IsNullOrWhiteSpace(timestamp))
            return TimeSpan.Zero;

        // Split by either ':' or '.'
        var parts = timestamp.Contains(':')
            ? timestamp.Split(':')
            : timestamp.Split('.');

        // Try parsing all parts as integers
        var success = parts.All(p => int.TryParse(p, out _));
        if (!success) return TimeSpan.Zero;

        var numbers = parts.Select(int.Parse).ToList();

        // Reverse to work from seconds up (e.g., ["1", "30"] becomes 30s + 1m)
        numbers.Reverse();

        var seconds = numbers.ElementAtOrDefault(0);
        var minutes = numbers.ElementAtOrDefault(1);
        var hours = numbers.ElementAtOrDefault(2);

        // Normalize overflow (e.g., 90s => 1m 30s)
        minutes += seconds / 60;
        seconds %= 60;

        hours += minutes / 60;
        minutes %= 60;

        return new TimeSpan(hours, minutes, seconds);
    }
}