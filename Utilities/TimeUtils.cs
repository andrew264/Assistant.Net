using Discord;

namespace Assistant.Net.Utilities;

public static class TimeUtils
{
    public static string GetLongDateTime(DateTimeOffset time)
        => $"{new TimestampTag(time, TimestampTagStyles.LongDateTime)}";
    
    public static string GetRelativeTime(DateTimeOffset time)
        => $"{new TimestampTag(time, TimestampTagStyles.Relative)}";
}

