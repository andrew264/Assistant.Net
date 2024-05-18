using Assistant.Net.Utils;
using Newtonsoft.Json;
using System.Net.Http.Json;
using System.Text;

namespace Assistant.Net.Services;

public class MicrosoftTranslatorService(HttpClient client, Config config)
{
    public readonly string ENDPOINT = "https://api.cognitive.microsofttranslator.com/translate?api-version=3.0";
    public readonly HttpClient _client = client;
    public readonly MicrosoftTranslatorConfig _config = config.translator;

    public async Task<string> TranslateAsync(string textToTranslate, string to, string? from = null)
    {
        var body = new object[] { new { Text = textToTranslate } };
        var requestBody = JsonConvert.SerializeObject(body);

        var route = $"{ENDPOINT}&to={to}";
        if (from != null)
        {
            route += $"&from={from}";
        }

        using var request = new HttpRequestMessage();
        request.Method = HttpMethod.Post;
        request.RequestUri = new Uri(route);
        request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        request.Headers.Add("Ocp-Apim-Subscription-Key", _config.key);
        request.Headers.Add("Ocp-Apim-Subscription-Region", _config.region);

        var response = await _client.SendAsync(request);
        var responseJson = await response.Content.ReadFromJsonAsync<List<TranslationResponse>>();
        if (responseJson == null || responseJson.Count == 0)
        {
            return "No translation found";
        }
        return responseJson[0].Translations[0].Text;
    }
}

internal class DetectedLanguage
{
    [JsonProperty("language")]
    public string? Language { get; set; }

    [JsonProperty("score")]
    public double? Score { get; set; }
}

internal class Translation
{
    [JsonProperty("text")]
    public required string Text { get; set; }

    [JsonProperty("to")]
    public string? To { get; set; }
}

internal class TranslationResponse
{
    [JsonProperty("detectedLanguage")]
    public DetectedLanguage? DetectedLanguage { get; set; }

    [JsonProperty("translations")]
    public required List<Translation> Translations { get; set; }
}