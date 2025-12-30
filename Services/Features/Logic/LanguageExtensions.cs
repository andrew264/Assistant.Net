using Assistant.Net.Services.Features.Models;

namespace Assistant.Net.Services.Features.Logic;

public static class LanguageExtensions
{
    public static string ToLanguageCode(this Language lang)
    {
        return lang switch
        {
            Language.English => "en",
            Language.Japanese => "ja",
            Language.Tamil => "ta",
            Language.Hindi => "hi",
            Language.Spanish => "es",
            Language.French => "fr",
            _ => "en" // Default to English
        };
    }
}