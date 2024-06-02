using Discord;
using Discord.Interactions;
using Lavalink4NET;
using Lavalink4NET.DiscordNet;
using Lavalink4NET.Integrations.SponsorBlock;
using Lavalink4NET.Integrations.SponsorBlock.Extensions;
using Lavalink4NET.Players;
using Lavalink4NET.Rest.Entities.Tracks;
using Lavalink4NET.Tracks;
using System.Collections.Immutable;

namespace Assistant.Net.Modules.Music;

[CommandContextType([InteractionContextType.Guild])]
public class PlayModule : InteractionModuleBase<SocketInteractionContext>
{
    public readonly IAudioService _audioService;
    private readonly ulong HomeGuildId;
    private readonly (string, ActivityType) DefaultActivity;
    private readonly ImmutableArray<SegmentCategory> categories = [
    SegmentCategory.Intro,
    SegmentCategory.Sponsor,
    SegmentCategory.SelfPromotion,
    SegmentCategory.Interaction,
    SegmentCategory.Outro,
    ];

    public PlayModule(IAudioService audioService, BotConfig config)
    {
        _audioService = audioService;
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

        var track = await _audioService.Tracks
            .LoadTrackAsync(query, TrackSearchMode.YouTube)
            .ConfigureAwait(false);

        if (track == null)
        {
            await FollowupAsync("No tracks found.").ConfigureAwait(false);
            return;
        }
        await player
            .UpdateSponsorBlockCategoriesAsync(categories)
            .ConfigureAwait(false);
        var position = await player.PlayAsync(track).ConfigureAwait(false);

        if (position == 0)
            await FollowupAsync($"Playing {ClickableLink(track)}").ConfigureAwait(false);
        else
            await FollowupAsync($"Added {ClickableLink(track)} to queue").ConfigureAwait(false);

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

        var result = await _audioService.Players
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
