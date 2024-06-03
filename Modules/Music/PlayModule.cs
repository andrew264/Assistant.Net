using Discord;
using Discord.Interactions;
using Lavalink4NET;
using Lavalink4NET.DiscordNet;
using Lavalink4NET.Integrations.Lavasearch;
using Lavalink4NET.Integrations.Lavasearch.Extensions;
using Lavalink4NET.Integrations.SponsorBlock;
using Lavalink4NET.Integrations.SponsorBlock.Extensions;
using Lavalink4NET.Players;
using Lavalink4NET.Rest.Entities.Tracks;
using Lavalink4NET.Tracks;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace Assistant.Net.Modules.Music;

[CommandContextType([InteractionContextType.Guild])]
public class PlayModule : InteractionModuleBase<SocketInteractionContext>
{
    public readonly IAudioService AudioService;
    private readonly ulong HomeGuildId;
    private readonly (string, ActivityType) DefaultActivity;
    private readonly ImmutableArray<SegmentCategory> categories = [
    SegmentCategory.Intro,
    SegmentCategory.Sponsor,
    SegmentCategory.SelfPromotion,
    SegmentCategory.Interaction,
    SegmentCategory.Outro,
    ];

    private readonly string validURLPattern = @"^(?!(https?:\/\/(www\.)?(youtube\.com|youtu\.be)\/|https?:\/\/(open|play|www)\.spotify\.com\/)).*$";
    private readonly string youtubeVideoPattern = @"^https?:\/\/(www\.)?(youtube\.com\/watch\?v=|youtu\.be\/)[A-Za-z0-9_-]+";
    private readonly string youtubePlaylistPattern = @"^https?:\/\/(www\.)?youtube\.com\/playlist\?list=[A-Za-z0-9_-]+";
    private readonly string spotifySongPattern = @"^https?:\/\/(open|play|www)\.spotify\.com\/track\/[A-Za-z0-9]+";
    private readonly string spotifyPlaylistPattern = @"^https?:\/\/(open|play|www)\.spotify\.com\/playlist\/[A-Za-z0-9]+";



    public PlayModule(IAudioService audioService, BotConfig config)
    {
        AudioService = audioService;
        HomeGuildId = config.client.home_guild_id;
        DefaultActivity = (config.client.activity_text, config.client.getActivityType());
    }

    [SlashCommand("play", description: "Plays music")]
    public async Task Play(string query)
    {
        await DeferAsync();

        var player = await GetPlayerAsync().ConfigureAwait(false);

        if (player == null)
            return;

        var tracks = await GetTrack(query).ConfigureAwait(false);

        if (tracks.Length == 0)
        {
            await FollowupAsync("No tracks found.").ConfigureAwait(false);
            return;
        }
        await player
            .UpdateSponsorBlockCategoriesAsync(categories)
            .ConfigureAwait(false);

        bool isFirstTrackInQueue = player.Queue.Count == 0;
        foreach (var track in tracks)
        {
            var _ = await player.PlayAsync(track).ConfigureAwait(false);
        }
        if (tracks!.Length == 1 && isFirstTrackInQueue)
            await FollowupAsync($"Playing {ClickableLink(tracks[0])}").ConfigureAwait(false);
        else if (tracks!.Length == 1)
            await FollowupAsync($"Added {ClickableLink(tracks[0])} to queue").ConfigureAwait(false);
        else
            await FollowupAsync($"Added {tracks.Length} songs to queue").ConfigureAwait(false);

    }

    private async Task<ImmutableArray<LavalinkTrack>> GetTrack(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];
        if (Regex.Match(query, validURLPattern).Success)
        {
            if (Regex.Match(query, youtubeVideoPattern).Success)
            {
                return await GetYouTubeTrack(query);
            }
            else if (Regex.Match(query, youtubePlaylistPattern).Success)
            {
                return await GetYouTubePlaylist(query);
            }
            else if (Regex.Match(query, spotifySongPattern).Success)
            {
                return await GetSpotifyTrack(query);
            }
            else if (Regex.Match(query, spotifyPlaylistPattern).Success)
            {
                return await GetSpotifyPlaylist(query);
            }
            else
            {
                return [];
            }
        }
        else
        {
            return await GetYouTubeTrack(query);
        }
    }

    private async Task<ImmutableArray<LavalinkTrack>> GetYouTubeTrack(string query)
    {
        var track = await AudioService.Tracks
            .LoadTrackAsync(query, TrackSearchMode.YouTube)
            .ConfigureAwait(false);
        return [track!];
    }

    private async Task<ImmutableArray<LavalinkTrack>> GetYouTubePlaylist(string query)
    {
        var playlist = await AudioService.Tracks
            .LoadTracksAsync(query, TrackSearchMode.YouTube)
            .ConfigureAwait(false);
        return playlist!.Tracks;
    }

    private async Task<ImmutableArray<LavalinkTrack>> GetSpotifyTrack(string query)
    {
        var searchResult = await AudioService.Tracks
            .SearchAsync(query: query,
                        loadOptions: new TrackLoadOptions(SearchMode: TrackSearchMode.Spotify),
                        categories: ImmutableArray.Create(SearchCategory.Track));
        var track = searchResult!.Tracks.FirstOrDefault();
        return [track!];
    }

    private async Task<ImmutableArray<LavalinkTrack>> GetSpotifyPlaylist(string query)
    {
        var searchResult = await AudioService.Tracks
            .SearchAsync(query: query,
                        loadOptions: new TrackLoadOptions(SearchMode: TrackSearchMode.Spotify),
                        categories: ImmutableArray.Create(SearchCategory.Playlist));
        var playlist = searchResult!.Playlists.FirstOrDefault();
        return [playlist!.SelectedTrack!];
    }


    private static string ClickableLink(LavalinkTrack? track)
    {
        if (track == null)
            return "Unknown";
        return $"[{track.Title}](<{track.Uri}>)";
    }

    private async ValueTask<CustomPlayer?> GetPlayerAsync(bool connectToVoiceChannel = true)
    {
        var retrieveOptions = new PlayerRetrieveOptions(
            ChannelBehavior: connectToVoiceChannel ? PlayerChannelBehavior.Join : PlayerChannelBehavior.None);

        static ValueTask<CustomPlayer> CreatePlayerAsync(IPlayerProperties<CustomPlayer, CustomPlayerOptions> properties, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(properties);

            return ValueTask.FromResult(new CustomPlayer(properties));
        }
        bool IsHomeGuild = Context.Guild.Id == HomeGuildId;
        var options = new CustomPlayerOptions()
        {
            IsHomeGuild = IsHomeGuild,
            DiscordClient = IsHomeGuild ? Context.Client : null,
            DefaultActivity = DefaultActivity
        };

        var result = await AudioService.Players
            .RetrieveAsync<CustomPlayer, CustomPlayerOptions>(Context, CreatePlayerAsync, options, retrieveOptions)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            var errorMessage = result.Status switch
            {
                PlayerRetrieveStatus.UserNotInVoiceChannel => "You are not connected to a voice channel.",
                PlayerRetrieveStatus.BotNotConnected => "The bot is currently not connected.",
                _ => "Unknown error.",
            };

            await FollowupAsync(errorMessage).ConfigureAwait(false);
            return null;
        }

        return result.Player;
    }
}
