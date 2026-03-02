namespace Assistant.Net.Options;

public sealed class DiscordOptions
{
    public const string SectionName = "Discord";

    public required string Token { get; set; }
    public string Prefix { get; set; } = "!";
    public ulong OwnerId { get; set; }
    public ulong HomeGuildId { get; set; }
    public List<ulong> TestGuilds { get; set; } = [];
    public ulong DmRecipientsCategory { get; set; }
    public string ResourcePath { get; set; } = "Resources";
}