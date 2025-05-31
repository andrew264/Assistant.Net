using Assistant.Net.Configuration;
using Discord;
using Discord.WebSocket;
using Lavalink4NET.Players.Queued;
using Microsoft.Extensions.Options;

namespace Assistant.Net.Modules.Music.Player;

public sealed record CustomPlayerOptions : QueuedLavalinkPlayerOptions, IOptions<CustomPlayerOptions>
{
    public ITextChannel? TextChannel { get; init; }
    public required DiscordSocketClient SocketClient { get; init; }
    public required Config ApplicationConfig { get; init; }
    public CustomPlayerOptions Value => this;
}