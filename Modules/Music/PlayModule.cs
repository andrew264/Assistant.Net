using Assistant.Net.Configuration;
using Assistant.Net.Modules.Music.Player;
using Assistant.Net.Services;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Lavalink4NET;
using Lavalink4NET.Clients;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.Rest.Entities.Tracks;
using Lavalink4NET.Tracks;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Music;

public class PlayModule(
    IAudioService audioService,
    ILogger<PlayModule> logger,
    Config config,
    MusicHistoryService musicHistoryService)
    : ModuleBase<SocketCommandContext>
{
    private static string Clickable(string title, Uri? uri) => $"[{title}](<{uri?.AbsoluteUri}>)";
    private static string Clickable(string title, string? uri) => $"[{title}](<{uri}>)";

    private async ValueTask<CustomPlayer?> GetPlayerAsyncPrefix(bool connectToVoiceChannel = true)
    {
        if (Context.User is not SocketGuildUser guildUser)
        {
            await ReplyAsync("You must be in a guild to use this command.");
            return null;
        }

        var voiceChannelId = guildUser.VoiceChannel?.Id;
        if (connectToVoiceChannel && voiceChannelId is null)
        {
            await ReplyAsync("You must be connected to a voice channel to use this command.");
            return null;
        }

        var retrieveOptions = new PlayerRetrieveOptions(
            connectToVoiceChannel ? PlayerChannelBehavior.Join : PlayerChannelBehavior.None,
            connectToVoiceChannel ? MemberVoiceStateBehavior.RequireSame : MemberVoiceStateBehavior.Ignore
        );

        var playerOptions = new CustomPlayerOptions
        {
            TextChannel = Context.Channel as ITextChannel ??
                          throw new InvalidOperationException("Command invoked outside a valid text channel."),
            SocketClient = Context.Client,
            ApplicationConfig = config,
            InitialVolume = await musicHistoryService.GetGuildVolumeAsync(Context.Guild.Id)
        };

        var result = await audioService.Players.RetrieveAsync<CustomPlayer, CustomPlayerOptions>(
            Context.Guild.Id, voiceChannelId,
            static (props, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return ValueTask.FromResult(new CustomPlayer(props));
            },
            playerOptions,
            retrieveOptions);

        if (result.IsSuccess) return result.Player;

        var errorMessage = result.Status switch
        {
            PlayerRetrieveStatus.UserNotInVoiceChannel => "You are not connected to a voice channel.",
            PlayerRetrieveStatus.BotNotConnected => "The bot is currently not connected to a voice channel.",
            PlayerRetrieveStatus.VoiceChannelMismatch => "You must be in the same voice channel as the bot.",
            PlayerRetrieveStatus.PreconditionFailed => "The bot is already connected to a different voice channel.",
            _ => "An unknown error occurred while retrieving the player."
        };

        logger.LogWarning(
            "Failed to retrieve player for Guild {GuildId} by User {UserId}. Status: {Status} (Prefix Command)",
            Context.Guild.Id, Context.User.Id, result.Status);

        await ReplyAsync(errorMessage);
        return null;
    }

    private async Task StartPlaybackIfNeededPrefix(CustomPlayer player)
    {
        if (player.State is PlayerState.NotPlaying or PlayerState.Destroyed)
        {
            var nextTrack = await player.Queue.TryDequeueAsync();
            if (nextTrack != null)
            {
                logger.LogInformation("[Player:{GuildId}] (Prefix) Starting playback with track: {TrackTitle}",
                    player.GuildId, nextTrack.Track?.Title);
                await player.PlayAsync(nextTrack, false);
            }
            else
            {
                logger.LogDebug("[Player:{GuildId}] (Prefix) Tried to start playback, but queue is empty.",
                    player.GuildId);
            }
        }
    }

    private async Task HandleSearchResultsPrefixAsync(IReadOnlyList<LavalinkTrack> tracks,
        SocketCommandContext commandContext)
    {
        var topTracks = tracks.Take(5).ToList();
        if (topTracks.Count == 0)
        {
            await ReplyAsync("No search results found.");
            return;
        }

        var embed = new EmbedBuilder()
            .WithTitle("🔎 Search Results")
            .WithColor(Color.Blue)
            .WithDescription("Select a track to add to the queue:");

        var components = new ComponentBuilder();
        for (var i = 0; i < topTracks.Count; i++)
        {
            var track = topTracks[i];
            var title = track.Title.Length > 50 ? track.Title[..47] + "..." : track.Title;
            var fieldDescription =
                $"{i + 1}. {Clickable(title, track.Uri)} by {track.Author} ({track.Duration:mm\\:ss})";
            embed.AddField("\u200B", fieldDescription);

            var customId = $"assistant:play_search:{commandContext.User.Id}:{track.Uri?.ToString() ?? string.Empty}";
            if (customId.Length <= 100)
                components.WithButton((i + 1).ToString(), customId, ButtonStyle.Secondary);
            else
                embed.Fields[i].Value += "\n*(Cannot be selected via button due to URI length)*";
        }

        await commandContext.Channel.SendMessageAsync(embed: embed.Build(), components: components.Build());
    }


    [Command("play", RunMode = RunMode.Async)]
    [Alias("p")]
    [Summary("Plays music, adds to queue, or controls playback.")]
    public async Task PlayAsync([Remainder] string? query = null)
    {
        var player = await GetPlayerAsyncPrefix(!string.IsNullOrWhiteSpace(query));
        if (player is null) return;

        if (string.IsNullOrWhiteSpace(query))
        {
            if (player.Queue.IsEmpty && player.CurrentTrack is null)
            {
                await ReplyAsync("The queue is empty. Please provide a song name or URL to play.");
                return;
            }

            switch (player.State)
            {
                case PlayerState.Playing:
                    await player.PauseAsync();
                    await ReplyAsync("Player paused.");
                    logger.LogInformation("[Player:{GuildId}] (Prefix) Paused playback by {User}", player.GuildId,
                        Context.User);
                    break;
                case PlayerState.Paused:
                    await player.ResumeAsync();
                    await ReplyAsync("Player resumed.");
                    logger.LogInformation("[Player:{GuildId}] (Prefix) Resumed playback by {User}", player.GuildId,
                        Context.User);
                    break;
                case PlayerState.Destroyed:
                case PlayerState.NotPlaying:
                default:
                    await ReplyAsync("Nothing to play or resume.");
                    break;
            }

            return;
        }

        logger.LogInformation("[Player:{GuildId}] (Prefix) Received play request by {User} with query: {Query}",
            player.GuildId, Context.User, query);

        var isUrl = Uri.TryCreate(query, UriKind.Absolute, out _);
        var searchMode = isUrl ? TrackSearchMode.None : TrackSearchMode.YouTube;
        TrackLoadResult result;

        try
        {
            result = await audioService.Tracks.LoadTracksAsync(query, searchMode);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "(Prefix) Exception during LoadTracksAsync for query '{Query}'", query);
            await ReplyAsync($"❌ An error occurred while searching: {ex.Message}");
            return;
        }

        if (result.IsPlaylist)
        {
            var playlist = result.Playlist!;
            var tracks = result.Tracks;
            if (tracks.IsEmpty)
            {
                await ReplyAsync($"Playlist '{playlist.Name}' is empty or could not be loaded.");
            }
            else
            {
                var trackItems = tracks.Select(t => new TrackQueueItem(t)).ToList();
                await player.Queue.AddRangeAsync(trackItems);
                await ReplyAsync(
                    $"Added {trackItems.Count} tracks from playlist '{Clickable(playlist.Name, query)}' to queue.");
                await StartPlaybackIfNeededPrefix(player);
            }
        }
        else if (searchMode != TrackSearchMode.None && !result.Tracks.IsEmpty)
        {
            await HandleSearchResultsPrefixAsync(result.Tracks, Context);
        }
        else if (result.Track is not null)
        {
            var loadedTrack = result.Track;
            await player.Queue.AddAsync(new TrackQueueItem(loadedTrack));
            await ReplyAsync($"Added to queue: {Clickable(loadedTrack.Title, loadedTrack.Uri)}");
            await StartPlaybackIfNeededPrefix(player);
        }
        else if (result.Exception is not null)
        {
            logger.LogError(
                "(Prefix) Track loading failed for query '{Query}'. Reason: {Reason}, Severity: {Severity}, Cause: {Cause}",
                query, result.Exception?.Message, result.Exception?.Severity, result.Exception?.Cause);
            await ReplyAsync($"❌ Failed to load track(s): {result.Exception?.Message ?? "Unknown error"}");
        }
        else
        {
            await ReplyAsync($"❌ No results found for: `{query}`");
        }
    }
}