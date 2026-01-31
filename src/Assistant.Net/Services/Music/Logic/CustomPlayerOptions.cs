using Discord.WebSocket;
using Lavalink4NET.Players.Queued;
using Microsoft.Extensions.Options;

namespace Assistant.Net.Services.Music.Logic;

public sealed record CustomPlayerOptions : QueuedLavalinkPlayerOptions, IOptions<CustomPlayerOptions>
{
    public required DiscordSocketClient SocketClient { get; init; }
    public CustomPlayerOptions Value => this;
}