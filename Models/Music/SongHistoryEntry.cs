namespace Assistant.Net.Models.Music;

public class SongHistoryEntry
{
    public string Title { get; set; } = null!;
    public string Uri { get; set; } = null!;
    public DateTime PlayedAt { get; set; }
    public ulong PlayedBy { get; set; } // UserId
    public TimeSpan Duration { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? Artist { get; set; }
}