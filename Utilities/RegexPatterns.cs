using System.Text.RegularExpressions;

namespace Assistant.Net.Utilities;

public static partial class RegexPatterns
{
    [GeneratedRegex(@"[\(\[].*?[\)\]]")]
    public static partial Regex Bracket();

    [GeneratedRegex(@"^<a?:\w+:\d+>$")]
    public static partial Regex DiscordEmoji();

    [GeneratedRegex(@"\[([^\]]+)\]")]
    public static partial Regex Link();

    [GeneratedRegex(@"(?<url>https?://\S+)", RegexOptions.IgnoreCase)]
    public static partial Regex Url();

    [GeneratedRegex("[^a-zA-Z0-9_-]")]
    public static partial Regex SanitizeText();
}