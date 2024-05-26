using Discord;
using Discord.Interactions;
using Lavalink4NET;
using Lavalink4NET.DiscordNet;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Vote;
using Lavalink4NET.Rest.Entities.Tracks;
using Lavalink4NET.Tracks;

namespace Assistant.Net.Modules.Music;

[CommandContextType([InteractionContextType.Guild])]
public class PlayModule : InteractionModuleBase<SocketInteractionContext>
{
    public required IAudioService _audioService { get; set; }

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

    private async ValueTask<VoteLavalinkPlayer?> GetPlayerAsync(bool connectToVoiceChannel = true)
    {
        var retrieveOptions = new PlayerRetrieveOptions(
            ChannelBehavior: connectToVoiceChannel ? PlayerChannelBehavior.Join : PlayerChannelBehavior.None);

        var result = await _audioService.Players
            .RetrieveAsync(Context, playerFactory: PlayerFactory.Vote, retrieveOptions: retrieveOptions)
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
