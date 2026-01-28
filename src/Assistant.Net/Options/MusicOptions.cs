namespace Assistant.Net.Options;

public sealed class MusicOptions
{
    public const string SectionName = "Music";

    public float DefaultVolume { get; set; } = 1.0f;
    public int MaxPlayerVolumePercent { get; set; } = 200;
    public double TitleSimilarityCutoff { get; set; } = 0.4;
}