using Assistant.Net.Options;
using Discord;
using Discord.WebSocket;
using Lavalink4NET.Players.Queued;
using Microsoft.Extensions.Options;

namespace Assistant.Net.Services.Music.Logic;

public sealed record CustomPlayerOptions : QueuedLavalinkPlayerOptions, IOptions<CustomPlayerOptions>
{
    public ITextChannel? TextChannel { get; init; }
    public required DiscordSocketClient SocketClient { get; init; }
    public required DiscordOptions DiscordOptions { get; init; }
    public required MusicOptions MusicOptions { get; init; }
    public CustomPlayerOptions Value => this;
}