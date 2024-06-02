using Discord.Interactions;
using Lavalink4NET;
using Lavalink4NET.Integrations.LyricsJava.Extensions;

namespace Assistant.Net.Modules.Music;

public class LyricsModule : InteractionModuleBase<SocketInteractionContext>
{
    public readonly IAudioService AudioService;

    public LyricsModule(IAudioService audioService)
    {
        AudioService = audioService;
        AudioService.UseLyricsJava();
    }

    static List<string> SplitString(string input)
    {
        var splitStrings = input.Split(new string[] { "\n\n" }, StringSplitOptions.None).ToList();
        var result = new List<string>();

        foreach (var str in splitStrings)
        {
            var lines = str.Split(new string[] { "\n" }, StringSplitOptions.None);
            if (lines.Length > 12)
            {
                var firstHalf = string.Join("\n", lines.Take(6));
                var secondHalf = string.Join("\n", lines.Skip(6).Take(6));
                result.Add(firstHalf);
                result.Add(secondHalf);
            }
            else
            {
                result.Add(str);
            }
        }

        return result;
    }

    [SlashCommand("lyrics", description: "Get lyrics for the song you are listening to")]
    public async Task Lyrics([Summary(description: "Name of the Song")] string query = "")
    {
        await DeferAsync();
        List<string> lyricsList = new();
        if (!string.IsNullOrEmpty(query))
        {
            var lyrics = await AudioService.Tracks
                .GetGeniusLyricsAsync(query)
                .ConfigureAwait(false);
            if (lyrics != null)
                lyricsList = SplitString(lyrics!.Text);
        }
        if (lyricsList.Count == 0)
        {
            await FollowupAsync("No lyrics found.")
                        .ConfigureAwait(false);
            return;
        }
        Console.WriteLine(lyricsList[0]);
        await FollowupAsync(lyricsList[0])
                        .ConfigureAwait(false);
    }
}
