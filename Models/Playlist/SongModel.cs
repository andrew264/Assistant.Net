namespace Assistant.Net.Models.Playlist;

public class SongModel
{
    public string Title { get; init; } = null!;

    public string Artist { get; init; } = null!;

    public string Uri { get; init; } = null!;

    public double Duration { get; init; }

    public string? Thumbnail { get; init; }

    public string Source { get; init; } = "other";
}