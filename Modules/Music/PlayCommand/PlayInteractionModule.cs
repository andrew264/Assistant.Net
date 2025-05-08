using Assistant.Net.Configuration;
using Assistant.Net.Modules.Music.Autocomplete;
using Assistant.Net.Modules.Music.Player;
using Assistant.Net.Services;
using Discord;
using Discord.Interactions;
using Lavalink4NET;
using Lavalink4NET.Clients;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.Rest.Entities.Tracks;
using Lavalink4NET.Tracks;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Music.PlayCommand;

[CommandContextType(InteractionContextType.Guild)]
public class PlayInteractionModule(
    IAudioService audioService,
    ILogger<PlayInteractionModule> logger,
    MusicHistoryService musicHistoryService,
    Config config)
    : InteractionModuleBase<SocketInteractionContext>
{
    private static string Clickable(string title, Uri? uri) => $"[{title}](<{uri?.AbsoluteUri}>)";
    private static string Clickable(string title, string? uri) => $"[{title}](<{uri}>)";

    private async Task RespondOrFollowupAsync(
        string? text = null,
        bool ephemeral = false,
        Embed? embed = null,
        MessageComponent? components = null)
    {
        if (Context.Interaction.HasResponded)
            await FollowupAsync(text, ephemeral: ephemeral, embed: embed, components: components);
        else
            await RespondAsync(text, ephemeral: ephemeral, embed: embed, components: components);
    }

    private async ValueTask<CustomPlayer?> GetPlayerAsync(bool connectToVoiceChannel = true)
    {
        if (Context.User is not IGuildUser guildUser)
        {
            await RespondOrFollowupAsync("You must be in a guild to use this command", true);
            return null;
        }

        var voiceChannelId = guildUser.VoiceChannel?.Id;
        if (connectToVoiceChannel && voiceChannelId is null)
        {
            await RespondOrFollowupAsync("You must be connected to a voice channel to use this command.", true);
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

        logger.LogWarning("Failed to retrieve player for Guild {GuildId} by User {UserId}. Status: {Status}",
            Context.Guild.Id, Context.User.Id, result.Status);

        await RespondOrFollowupAsync(errorMessage, true);
        return null;
    }

    private async Task HandleSearchResults(IReadOnlyList<LavalinkTrack> tracks)
    {
        var topTracks = tracks.Take(5).ToList();
        if (topTracks.Count == 0)
        {
            await RespondOrFollowupAsync("No search results found.", true);
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
            embed.AddField("\u200B",
                $"{i + 1}: {Clickable(title, track.Uri)} by {track.Author} ({track.Duration:mm\\:ss})");


            var customId = $"assistant:play_search:{Context.User.Id}:{track.Uri}";
            if (customId.Length <= 100)
                components.WithButton((i + 1).ToString(), customId, ButtonStyle.Secondary);
            else
                embed.Fields[i].Value += "\n*(Cannot be selected via button due to URI length)*";
        }

        await RespondOrFollowupAsync(embed: embed.Build(), components: components.Build());
    }

    private async Task StartPlaybackIfNeeded(CustomPlayer player)
    {
        if (player.State is PlayerState.NotPlaying or PlayerState.Destroyed)
        {
            var nextTrack = await player.Queue.TryDequeueAsync();
            if (nextTrack != null)
            {
                logger.LogInformation("[Player:{GuildId}] Starting playback with track: {TrackTitle}", player.GuildId,
                    nextTrack.Track?.Title);
                await player.PlayAsync(nextTrack, false);
            }
            else
            {
                logger.LogDebug("[Player:{GuildId}] Tried to start playback, but queue is empty.", player.GuildId);
            }
        }
    }

    [SlashCommand("play", "Plays music.", runMode: RunMode.Async)]
    public async Task PlaySlashCommand(
        [Summary(description: "Song name or URL to play.")] [Autocomplete(typeof(MusicQueryAutocompleteProvider))]
        string? query = null)
    {
        await DeferAsync();

        var player = await GetPlayerAsync(!string.IsNullOrWhiteSpace(query));
        if (player is null) return;

        if (string.IsNullOrWhiteSpace(query))
        {
            if (player.Queue.IsEmpty && player.CurrentTrack is null)
            {
                await FollowupAsync("The queue is empty. Please provide a song name or URL to play.", ephemeral: true);
                return;
            }

            var track = player.CurrentTrack;

            if (track == null)
            {
                await FollowupAsync("I am not playing anything right now.", ephemeral: true);
                return;
            }

            switch (player.State)
            {
                case PlayerState.Playing:
                    await player.PauseAsync();
                    await FollowupAsync($"Paused: {Clickable(track.Title, track.Uri)}", ephemeral: false);
                    logger.LogInformation("[Player:{GuildId}] Paused playback by {User}", player.GuildId, Context.User);
                    break;
                case PlayerState.Paused:
                    await player.ResumeAsync();
                    await FollowupAsync($"Resumed: {Clickable(track.Title, track.Uri)}", ephemeral: false);
                    logger.LogInformation("[Player:{GuildId}] Resumed playback by {User}", player.GuildId,
                        Context.User);
                    break;
                case PlayerState.Destroyed:
                case PlayerState.NotPlaying:
                default:
                    await FollowupAsync("I am not playing anything right now.", ephemeral: true);
                    break;
            }

            return;
        }

        logger.LogInformation("[Player:{GuildId}] Received play request by {User} with query: {Query}", player.GuildId,
            Context.User, query);

        var isUrl = Uri.TryCreate(query, UriKind.Absolute, out _);
        var searchMode = isUrl ? TrackSearchMode.None : TrackSearchMode.YouTube;
        TrackLoadResult result;
        try
        {
            result = await audioService.Tracks.LoadTracksAsync(query, searchMode);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception during LoadTracksAsync for query '{Query}'", query);
            await RespondOrFollowupAsync($"❌ An error occurred while searching: {ex.Message}", true);
            return;
        }

        if (result.IsPlaylist)
        {
            var playlist = result.Playlist!;
            var tracks = result.Tracks;
            if (tracks.IsEmpty)
            {
                await RespondOrFollowupAsync($"Playlist '{playlist.Name}' is empty or could not be loaded.", true);
            }
            else
            {
                var trackItems = tracks.Select(t => new TrackQueueItem(t)).ToList();
                await player.Queue.AddRangeAsync(trackItems);
                await RespondOrFollowupAsync(
                    $"Added {trackItems.Count} tracks from playlist '{Clickable(playlist.Name, query)}' to queue.");
                await StartPlaybackIfNeeded(player);
            }
        }
        else if (searchMode != TrackSearchMode.None && !result.Tracks.IsEmpty)
        {
            await HandleSearchResults(result.Tracks);
        }
        else if (result.Track is not null)
        {
            var loadedTrack = result.Track;
            await player.Queue.AddAsync(new TrackQueueItem(loadedTrack));
            await RespondOrFollowupAsync($"Added to queue: {Clickable(loadedTrack.Title, loadedTrack.Uri)}");
            await StartPlaybackIfNeeded(player);
        }
        else if (result.Exception is not null)
        {
            logger.LogError(
                "Track loading failed for query '{Query}'. Reason: {Reason}, Severity: {Severity}, Cause: {Cause}",
                query, result.Exception?.Message, result.Exception?.Severity, result.Exception?.Cause);
            await RespondOrFollowupAsync($"❌ Failed to load track(s): {result.Exception?.Message ?? "Unknown error"}");
        }
        else
        {
            await RespondOrFollowupAsync($"❌ No results found for: `{query}`");
        }
    }


    [ComponentInteraction("assistant:play_search:*:*", true)]
    public async Task HandleSearchResultSelection(ulong requesterId, string uri)
    {
        if (Context.User.Id != requesterId)
        {
            await RespondAsync("Only the person who started the search can select a track.", ephemeral: true);
            return;
        }

        await DeferAsync();

        var player = await GetPlayerAsync();
        if (player is null) return;

        try
        {
            var result = await audioService.Tracks.LoadTracksAsync(uri, TrackSearchMode.None);
            if (result.Track is null)
                throw new Exception(result.Exception?.Message ?? "Unknown error");

            await player.Queue.AddAsync(new TrackQueueItem(result.Track));
            await ModifyOriginalResponseAsync(props =>
            {
                props.Content = $"Added to queue: {Clickable(result.Track.Title, result.Track.Uri)}";
                props.Embed = null;
                props.Components = new ComponentBuilder().Build();
            });
            await StartPlaybackIfNeeded(player);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load selected track: {Uri}", uri);
            await ModifyOriginalResponseAsync(props =>
            {
                props.Content = "❌ Failed to load the selected track.";
                props.Embed = null;
                props.Components = new ComponentBuilder().Build();
            });
        }
    }
}