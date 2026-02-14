using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Assistant.Net.Utilities;

namespace Assistant.Net.Models.UrbanDictionary;

public partial class UrbanDictionaryEntry
{
    private const string UdBaseUrl = "https://www.urbandictionary.com/define.php?term={0}";

    [JsonInclude]
    [JsonPropertyName("definition")]
    public string Definition { get; private set; } = "No definition found.";

    [JsonInclude]
    [JsonPropertyName("example")]
    public string Example { get; private set; } = string.Empty;

    [JsonPropertyName("word")] public string Word { get; set; } = string.Empty;

    [JsonPropertyName("author")] public string Author { get; set; } = "Unknown";

    [JsonPropertyName("permalink")] public string Permalink { get; set; } = string.Empty;

    [JsonIgnore] public string FormattedDefinition => FormatLinks(Definition);

    [JsonIgnore]
    public string FormattedExample =>
        FormatLinks(NormalizeNewlineRegex().Replace(Example, "\n"));

    private static string FormatLinks(string text)
    {
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