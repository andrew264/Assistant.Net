using Assistant.Net.Models.Music;
using Assistant.Net.Services.Music;
using Assistant.Net.Utilities;
using Discord;
using Discord.Commands;
using Lavalink4NET.Clients;
using Lavalink4NET.Players;
using Lavalink4NET.Tracks;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Music.Prefix;

public class PlayModule(MusicService musicService, ILogger<PlayModule> logger)
    : MusicPrefixModuleBase(musicService, logger)
{
    private async Task HandleSearchResultsUi(IReadOnlyList<LavalinkTrack> tracks, string originalQuery)
    {
        var topTracks = tracks.Take(5).ToList();
        if (topTracks.Count == 0)
        {
            await ReplyAsync("No search results found.").ConfigureAwait(false);
            return;
        }

        var container = new ContainerBuilder()
            .WithTextDisplay(new TextDisplayBuilder($"**🔎 Search Results for:** `{originalQuery.Truncate(100)}`"))
            .WithTextDisplay(new TextDisplayBuilder($"*Select a track to add to the queue, <@{Context.User.Id}>:*"))
            .WithSeparator();

        foreach (var track in topTracks)
        {
            var trackInfo = new SectionBuilder()
                .AddComponent(new TextDisplayBuilder($"**{track.Title.Truncate(80)}**\nby {track.Author.Truncate(80)}"))
                .AddComponent(
                    new TextDisplayBuilder(
                        $"Duration: {track.Duration:mm\\:ss} | Source: {track.SourceName ?? "Unknown"}"));

            var customId = $"play_search:{Context.User.Id}:{track.Uri?.ToString() ?? string.Empty}";
            if (customId.Length <= 100 && track.Uri != null)
            {
                trackInfo.WithAccessory(new ButtonBuilder("Select", customId, ButtonStyle.Secondary));
            }
            else
            {
                var reason = track.Uri == null ? "(Missing URI)" : "(URI too long for button)";
                trackInfo.AddComponent(new TextDisplayBuilder($"*Cannot be selected via button {reason}*"));
            }

            container.AddComponent(trackInfo);
        }

        // For prefix commands, a cancel button that deletes the message is appropriate.
        container.WithActionRow(new ActionRowBuilder()
            .WithButton("Cancel", $"play_search_cancel:{Context.User.Id}", ButtonStyle.Danger));

        var components = new ComponentBuilderV2().WithContainer(container).Build();

        await Context.Channel.SendMessageAsync(components: components, flags: MessageFlags.ComponentsV2)
            .ConfigureAwait(false);
    }

    [Command("play", RunMode = RunMode.Async)]
    [Alias("p")]
    [Summary("Plays music, adds to queue, or controls playback.")]
    public async Task PlayAsync([Remainder] string? query = null)
    {
        var connectToVoice = !string.IsNullOrWhiteSpace(query);
        var (player, isError) =
            await GetVerifiedPlayerAsync(connectToVoice ? PlayerChannelBehavior.Join : PlayerChannelBehavior.None,
                    connectToVoice ? MemberVoiceStateBehavior.RequireSame : MemberVoiceStateBehavior.Ignore)
                .ConfigureAwait(false);

        if (isError || player is null) return;

        if (string.IsNullOrWhiteSpace(query)) // No query means pause/resume
        {
            var (_, message) = await MusicService.PauseOrResumeAsync(player, Context.User).ConfigureAwait(false);
            await ReplyAsync(message).ConfigureAwait(false);
            return;
        }

        var loadResult = await MusicService.LoadAndQueueTrackAsync(player, query, Context.User).ConfigureAwait(false);

        switch (loadResult.Status)
        {
            case TrackLoadStatus.TrackLoaded:
                await ReplyAsync(
                        $"Added to queue: {loadResult.LoadedTrack!.Title.AsMarkdownLink(loadResult.LoadedTrack.Uri?.ToString())}")
                    .ConfigureAwait(false);
                await MusicService.StartPlaybackIfNeededAsync(player).ConfigureAwait(false);
                break;
            case TrackLoadStatus.PlaylistLoaded:
                await ReplyAsync(
                        $"Added {loadResult.Tracks.Count} tracks from playlist '{loadResult.PlaylistInformation!.Name.AsMarkdownLink(loadResult.OriginalQuery)}' to queue.")
                    .ConfigureAwait(false);
                await MusicService.StartPlaybackIfNeededAsync(player).ConfigureAwait(false);
                break;
            case TrackLoadStatus.SearchResults:
                await HandleSearchResultsUi(loadResult.Tracks, loadResult.OriginalQuery).ConfigureAwait(false);
                break;
            case TrackLoadStatus.NoMatches:
                await ReplyAsync($"❌ No results found for: `{loadResult.OriginalQuery}`").ConfigureAwait(false);
                break;
            case TrackLoadStatus.LoadFailed:
            default:
                await ReplyAsync($"❌ Failed to load track(s): {loadResult.ErrorMessage ?? "Unknown error"}")
                    .ConfigureAwait(false);
                break;
        }
    }
}