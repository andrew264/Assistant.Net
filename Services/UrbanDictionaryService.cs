using System.Text.Json;
using System.Text.RegularExpressions;

namespace Assistant.Net.Services;

public class UrbanDictionaryService
{
    public readonly string API_URL = "https://api.urbandictionary.com/v0/define?term=";
    public readonly string API_RANDOM_URL = "https://api.urbandictionary.com/v0/random";
    public readonly HttpClient _client;

    public UrbanDictionaryService(HttpClient client)
    {
        _client = client;
    }
    public async Task<string> GetDefinitionAsync(string word)
    {
        string url = string.IsNullOrWhiteSpace(word) ? API_RANDOM_URL : API_URL + Uri.EscapeDataString(word);

        var response = await _client.GetAsync(url);
        var content = await response.Content.ReadAsStringAsync();
        var data = JsonSerializer.Deserialize<RootObject>(content);
        if (data is null || data.list.Count == 0)
            return "No definition found.";

        Random random = new();
        var definition = data.list[random.Next(Math.Min(5, data.list.Count))];
        return definition.markdown();
    }
}

internal class Definition
{
    public string definition { get; set; }
    public string permalink { get; set; }
    public int thumbs_up { get; set; }
    public string author { get; set; }
    public string word { get; set; }
    public int defid { get; set; }
    public string current_vote { get; set; }
    public DateTime written_on { get; set; }
    public string example { get; set; }
    public int thumbs_down { get; set; }

    private string urlForWord(string word)
    {
        return $"<https://www.urbandictionary.com/define.php?term={Uri.EscapeDataString(word)}>";
    }

    private string format(string text)
    {
        string rxPattern = @"\[(.*?)\]";
        string formatted = Regex.Replace(text, rxPattern, delegate (Match match)
        {
            string word = match.Groups[1].Value;
            return $"[{word}]({urlForWord(word)})";
        });
        return formatted;
    }

    public string markdown()
    {
        string output = $"# Define: [{word}]({urlForWord(word)})\n{format(definition)}";
        if (!string.IsNullOrWhiteSpace(example))
            output += $"\n\n**Example:**\n{format(example)}";
        output += $"\n\n_{thumbs_up}_ 👍 • _{thumbs_down}_ 👎";
        return output;
    }
}

internal class RootObject
{
    public List<Definition> list { get; set; }
}