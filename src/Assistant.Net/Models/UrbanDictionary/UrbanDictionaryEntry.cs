using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Assistant.Net.Utilities;

namespace Assistant.Net.Models.UrbanDictionary;

public partial class UrbanDictionaryEntry
{
    private const string UdBaseUrl = "https://www.urbandictionary.com/define.php?term={0}";

    [JsonPropertyName("definition")]
    public string Definition
    {
        get;
        set => field = FormatUdLinks(value);
    } = "No definition found.";

    [JsonPropertyName("example")]
    public string Example
    {
        get;
        set => field = FormatUdLinks(NormalizeNewlineRegex().Replace(value, "\n"));
    } = string.Empty;

    [JsonPropertyName("word")] public string Word { get; set; } = string.Empty;

    [JsonPropertyName("author")] public string Author { get; set; } = "Unknown";

    [JsonPropertyName("permalink")] public string Permalink { get; set; } = string.Empty;

    private static string FormatUdLinks(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        return RegexPatterns.Link().Replace(text, match =>
        {
            var term = match.Groups[1].Value;
            var encodedTerm = Uri.EscapeDataString(term);
            return $"[{term}](<{string.Format(UdBaseUrl, encodedTerm)}>)";
        });
    }

    [GeneratedRegex(@"\r\n|\r")]
    private static partial Regex NormalizeNewlineRegex();
}