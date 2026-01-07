using Assistant.Net.Models.Music;
using Assistant.Net.Modules.Shared.Autocomplete;
using Assistant.Net.Services.Music;
using Assistant.Net.Utilities;
using Discord;
using Discord.Interactions;
using Lavalink4NET.Clients;
using Lavalink4NET.Players;
using Lavalink4NET.Tracks;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Music.Interaction;

[CommandContextType(InteractionContextType.Guild)]
public class PlayInteractionModule(MusicService musicService, ILogger<PlayInteractionModule> logger)
    : MusicInteractionModuleBase(musicService, logger)
{
    private async Task HandleSearchResultsUi(IReadOnlyList<LavalinkTrack> tracks, string originalQuery)
    {
        var topTracks = tracks.Take(5).ToList();
        if (topTracks.Count == 0)
        {
            await RespondOrFollowupAsync("No search results found.", true).ConfigureAwait(false);
            return;
        }

        var container = new ContainerBuilder()
            .WithTextDisplay(new TextDisplayBuilder($"**🔎 Search Results for:** `{originalQuery.Truncate(100)}`"))
            .WithTextDisplay(new TextDisplayBuilder("*Select a track to add to the queue:*"))
            .WithSeparator();

        foreach (var track in topTracks)
        {
            var trackInfo = new SectionBuilder()
                .AddComponent(new TextDisplayBuilder(
                    $"**{track.Title.Truncate(80)}**\nby {track.Author.Truncate(80)}"))
                .AddComponent(new TextDisplayBuilder(
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

        container.WithActionRow(new ActionRowBuilder()
            .WithButton("Cancel", $"play_search_cancel:{Context.User.Id}", ButtonStyle.Danger));

        var components = new ComponentBuilderV2().WithContainer(container).Build();

        await RespondOrFollowupAsync(components: components, ephemeral: true)
            .ConfigureAwait(false);
    }

    [SlashCommand("play", "Plays music.", runMode: RunMode.Async)]
    public async Task PlaySlashCommand(
        [Summary(description: "Song name or URL to play.")] [Autocomplete(typeof(MusicQueryAutocompleteProvider))]
        string? query = null)
    {
        await DeferAsync().ConfigureAwait(false);

        var connectToVoice = !string.IsNullOrWhiteSpace(query);
        var (player, isError) = await GetVerifiedPlayerAsync(
            connectToVoice ? PlayerChannelBehavior.Join : PlayerChannelBehavior.None,
            connectToVoice ? MemberVoiceStateBehavior.RequireSame : MemberVoiceStateBehavior.Ignore
        ).ConfigureAwait(false);

        if (isError || player is null) return;

        if (string.IsNullOrWhiteSpace(query)) // No query means pause/resume
        {
            var (success, message) = await MusicService.PauseOrResumeAsync(player, Context.User).ConfigureAwait(false);
            await RespondOrFollowupAsync(message, !success).ConfigureAwait(false);
            return;
        }

        var loadResult = await MusicService.LoadAndQueueTrackAsync(player, query, Context.User).ConfigureAwait(false);

        switch (loadResult.Status)
        {
            case TrackLoadStatus.TrackLoaded:
                await RespondOrFollowupAsync(
                        $"Added to queue: {loadResult.LoadedTrack!.Title.AsMarkdownLink(loadResult.LoadedTrack.Uri?.ToString())}")
                    .ConfigureAwait(false);
                await MusicService.StartPlaybackIfNeededAsync(player).ConfigureAwait(false);
                break;
            case TrackLoadStatus.PlaylistLoaded:
                await RespondOrFollowupAsync(
                        $"Added {loadResult.Tracks.Count} tracks from playlist '{loadResult.PlaylistInformation!.Name.AsMarkdownLink(loadResult.OriginalQuery)}' to queue.")
                    .ConfigureAwait(false);
                await MusicService.StartPlaybackIfNeededAsync(player).ConfigureAwait(false);
                break;
            case TrackLoadStatus.SearchResults:
                await HandleSearchResultsUi(loadResult.Tracks, loadResult.OriginalQuery).ConfigureAwait(false);
                break;
            case TrackLoadStatus.NoMatches:
                await RespondOrFollowupAsync($"❌ No results found for: `{loadResult.OriginalQuery}`", true)
                    .ConfigureAwait(false);
                break;
            case TrackLoadStatus.LoadFailed:
            default:
                await RespondOrFollowupAsync($"❌ Failed to load track(s): {loadResult.ErrorMessage ?? "Unknown error"}",
                    true).ConfigureAwait(false);
                break;
        }
    }

    [ComponentInteraction("play_search:*:*", true)]
    public async Task HandleSearchResultSelection(ulong requesterId, string uri)
    {
        if (Context.User.Id != requesterId)
        {
            await RespondAsync("Only the person who started the search can select a track.", ephemeral: true)
                .ConfigureAwait(false);
            return;
        }

        await DeferAsync().ConfigureAwait(false);

        var (player, isError) = await GetVerifiedPlayerAsync(
            PlayerChannelBehavior.Join,
            MemberVoiceStateBehavior.RequireSame
        ).ConfigureAwait(false);

        if (isError || player is null) return;

        var track = await MusicService.GetTrackFromSearchSelectionAsync(player, uri).ConfigureAwait(false);

        if (track is not null)
        {
            await player.Queue.AddAsync(new CustomTrackQueueItem(track, requesterId)).ConfigureAwait(false);

            var confirmationContainer = new ContainerBuilder()
                .WithTextDisplay(
                    new TextDisplayBuilder(
                        $"✅ **Added to queue:** {track.Title.AsMarkdownLink(track.Uri?.ToString())}"));

            var components = new ComponentBuilderV2().WithContainer(confirmationContainer).Build();

            await ModifyOriginalResponseAsync(props => { props.Components = components; }).ConfigureAwait(false);

            await MusicService.StartPlaybackIfNeededAsync(player).ConfigureAwait(false);
        }
        else
        {
            var errorContainer = new ContainerBuilder()
                .WithAccentColor(Color.Red)
                .WithTextDisplay(new TextDisplayBuilder("❌ **Failed to load the selected track.**"));
            var components = new ComponentBuilderV2().WithContainer(errorContainer).Build();

            await ModifyOriginalResponseAsync(props => { props.Components = components; }).ConfigureAwait(false);
        }
    }

    [ComponentInteraction("play_search_cancel:*", true)]
    public async Task HandleSearchCancel(ulong requesterId)
    {
        if (Context.User.Id != requesterId)
        {
            await RespondAsync("This is not your search.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync().ConfigureAwait(false);
        await (await GetOriginalResponseAsync().ConfigureAwait(false)).DeleteAsync().ConfigureAwait(false);
    }
}