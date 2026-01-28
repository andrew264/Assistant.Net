namespace Assistant.Net.Options;

public sealed class LavalinkOptions
{
    public const string SectionName = "Lavalink";

    public List<LavalinkNodeOptions> Nodes { get; set; } = [];
    public bool IsValid => Nodes.Count > 0;
}

public sealed class LavalinkNodeOptions
{
    public string Name { get; set; } = "default";
    public string Uri { get; set; } = "http://localhost:2333";
    public string Password { get; set; } = "youshallnotpass";
}