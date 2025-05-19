using System.Web;
using Assistant.Net.Utilities;
using Newtonsoft.Json;

namespace Assistant.Net.Models.UrbanDictionary;

public class UrbanDictionaryEntry
{
    private const string UdBaseUrl = "https://www.urbandictionary.com/define.php?term={0}";

    [JsonProperty("definition")] public string Definition { get; set; } = "No definition found.";

    [JsonProperty("example")] public string Example { get; set; } = string.Empty;

    [JsonProperty("word")] public string Word { get; set; } = string.Empty;

    [JsonProperty("author")] public string Author { get; set; } = "Unknown";

    [JsonProperty("permalink")] public string Permalink { get; set; } = string.Empty;

    [JsonProperty("thumbs_up")] public int ThumbsUp { get; set; }

    [JsonProperty("thumbs_down")] public int ThumbsDown { get; set; }

    [JsonProperty("defid")] public long DefId { get; set; }

    [JsonProperty("written_on")] public string WrittenOn { get; set; } = string.Empty;

    [JsonIgnore] // Don't serialize
    private string FormattedDefinition => FormatLinks(Definition);

    [JsonIgnore] private string FormattedExample => FormatLinks(Example.Replace("\r", ""));

    [JsonIgnore]
    public string Markdown
    {
        get
        {
            var formattedExampleSection = !string.IsNullOrWhiteSpace(FormattedExample)
                ? $"\n\n*Example:*\n{FormattedExample}"
                : "";

            return $"# Define: [{Word}](<{Permalink}>)\n\n" +
                   $"{FormattedDefinition}" +
                   $"{formattedExampleSection}\n\n" +
                   $"{ThumbsUp} 👍  •  {ThumbsDown} 👎";
        }
    }

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