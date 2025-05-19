namespace Assistant.Net.Utilities;

public static class StringExtensions
{
    public static string CapitalizeFirstLetter(this string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;
        if (input.Length == 1)
            return input.ToUpper();
        return char.ToUpper(input[0]) + input[1..];
    }

    public static string AsMarkdownLink(this string text, string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return text;
        var sanitizedText = text.Replace("[", "\\[").Replace("]", "\\]");
        return $"[{sanitizedText}](<{url}>)";
    }

    public static string Truncate(this string value, int maxLength, string truncationSuffix = "...")
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength) return value;
        return string.Concat(value.AsSpan(0, maxLength - truncationSuffix.Length), truncationSuffix);
    }

    public static List<string> SmartChunkSplitList(this string input) => StringSplitter.SplitString(input);

    public static string RemoveStuffInBrackets(this string input) => RegexPatterns.Bracket().Replace(input, "");
}