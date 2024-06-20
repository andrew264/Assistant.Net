using Discord;
using Discord.Commands;
using Lavalink4NET;
using Lavalink4NET.Integrations.SponsorBlock;
using Lavalink4NET.Integrations.SponsorBlock.Extensions;
using Lavalink4NET.Players;
using Lavalink4NET.Rest.Entities.Tracks;
using Lavalink4NET.Tracks;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace Assistant.Net.Modules.Music;
public class MusicControlModule : ModuleBase<SocketCommandContext>
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

    private readonly string validURLPattern = @"^(https?:\/\/)?([^\s:@]+:[^\s:@]*@)?([^\s:@\/]+)(:\d+)?(\/[^\s]*)?$";
    private readonly string youtubeVideoPattern = @"^https?:\/\/(www\.)?(youtube\.com\/watch\?v=|youtu\.be\/)[A-Za-z0-9_-]+";
    private readonly string youtubePlaylistPattern = @"^https?:\/\/(www\.)?youtube\.com\/playlist\?list=[A-Za-z0-9_-]+";
    private readonly string spotifySongPattern = @"^https?:\/\/(open|play|www)\.spotify\.com\/track\/[A-Za-z0-9]+";
    private readonly string spotifyPlaylistPattern = @"^https?:\/\/(open|play|www)\.spotify\.com\/playlist\/[A-Za-z0-9]+";

    public MusicControlModule(IAudioService audioService, BotConfig config)
    {
        AudioService = audioService;
        HomeGuildId = config.client.home_guild_id;
        DefaultActivity = (config.client.activity_text, config.client.getActivityType());
    }

    [Command("play", Aliases = ["p"], RunMode = RunMode.Async)]
    [RequireContext(ContextType.Guild)]
    public async Task PlayAsync([Remainder] string query)
    {
        if (query == null)
        {
            await ReplyAsync("Please provide a query to search for.");
            return;
        }
        var Channel = Context.User as IGuildUser;
        if (Channel == null)
        {
            await ReplyAsync("You must be in a voice channel to use this command.");
            return;
        }
        var player = await GetPlayerAsync(Channel.VoiceChannel.Id);

        if (player == null)
            return;

        var tracks = await GetTrack(query);

        if (tracks.Length == 0)
        {
            await ReplyAsync("No tracks found.");
            return;
        }

        await player
            .UpdateSponsorBlockCategoriesAsync(categories);

        bool isFirstTrackInQueue = player.Queue.Count == 0;
        foreach (var track in tracks)
        {
            var _ = await player.PlayAsync(track);
        }
        if (tracks!.Length == 1 && isFirstTrackInQueue)
            await Context.Channel.SendMessageAsync($"Playing {ClickableLink(tracks[0])}");
        else if (tracks!.Length == 1)
            await Context.Channel.SendMessageAsync($"Added {ClickableLink(tracks[0])} to queue");
        else
            await Context.Channel.SendMessageAsync($"Added {tracks.Length} songs to queue");
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
            .LoadTrackAsync(query, TrackSearchMode.Spotify);
        var track = searchResult!;
        return [track!];
    }

    private async Task<ImmutableArray<LavalinkTrack>> GetSpotifyPlaylist(string query)
    {
        var searchResult = await AudioService.Tracks
            .LoadTracksAsync(query, TrackSearchMode.Spotify);
        var playlist = searchResult!;
        return playlist!.Tracks;
    }


    private static string ClickableLink(LavalinkTrack? track)
    {
        if (track == null)
            return "Unknown";
        return $"[{track.Title}](<{track.Uri}>)";
    }

    private async ValueTask<CustomPlayer?> GetPlayerAsync(ulong ChannelID, bool connectToVoiceChannel = true)
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
            .RetrieveAsync<CustomPlayer, CustomPlayerOptions>(Context.Guild.Id, ChannelID, CreatePlayerAsync, options, retrieveOptions);

        if (!result.IsSuccess)
        {
            var errorMessage = result.Status switch
            {
                PlayerRetrieveStatus.UserNotInVoiceChannel => "You are not connected to a voice channel.",
                PlayerRetrieveStatus.BotNotConnected => "The bot is currently not connected.",
                _ => "Unknown error.",
            };

            await Context.Channel.SendMessageAsync(errorMessage);
            return null;
        }

        return result.Player;
    }
}