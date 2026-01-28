namespace Assistant.Net.Options;

public sealed class ExternalApiOptions
{
    public const string SectionName = "ExternalApis";

    public string? GeniusToken { get; set; }
    public string? YoutubeApiKey { get; set; }
    public string? TenorApiKey { get; set; }
}