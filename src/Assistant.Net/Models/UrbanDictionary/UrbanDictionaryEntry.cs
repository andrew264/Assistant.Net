using System.Text.Json.Serialization;
using System.Web;
using Assistant.Net.Utilities;

namespace Assistant.Net.Models.UrbanDictionary;

public class UrbanDictionaryEntry
{
    private const string UdBaseUrl = "https://www.urbandictionary.com/define.php?term={0}";

    [JsonPropertyName("definition")] private string Definition { get; } = "No definition found.";

    [JsonPropertyName("example")] private string Example { get; } = string.Empty;

    [JsonPropertyName("word")] public string Word { get; set; } = string.Empty;

    [JsonPropertyName("author")] public string Author { get; set; } = "Unknown";

    [JsonPropertyName("permalink")] public string Permalink { get; set; } = string.Empty;

    [JsonPropertyName("thumbs_up")] public int ThumbsUp { get; set; }

    [JsonPropertyName("thumbs_down")] public int ThumbsDown { get; set; }

    [JsonPropertyName("defid")] public long DefId { get; set; }

    [JsonPropertyName("written_on")] public string WrittenOn { get; set; } = string.Empty;

    [JsonIgnore] public string FormattedDefinition => FormatLinks(Definition);

    [JsonIgnore] public string FormattedExample => FormatLinks(Example.Replace("\r", ""));

    private static string FormatLinks(string text)
    {
        // Replace [term] with [term](<UD_URL_FOR_TERM>)
        return RegexPatterns.Link().Replace(text, match =>
        {
            var term = match.Groups[1].Value;
            var encodedTerm = HttpUtility.UrlEncode(term);
            return $"[{term}](<{string.Format(UdBaseUrl, encodedTerm)}>)";
        });
    }
}