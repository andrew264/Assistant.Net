using Assistant.Net.Models.Music;
using Assistant.Net.Modules.Music.Autocomplete;
using Assistant.Net.Modules.Music.Logic;
using Assistant.Net.Services.Music;
using Assistant.Net.Utilities;
using Discord;
using Discord.Interactions;
using Lavalink4NET.Clients;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.Tracks;

namespace Assistant.Net.Modules.Music.InteractionModules;

[CommandContextType(InteractionContextType.Guild)]
public class PlayInteractionModule(
    MusicService musicService
) : InteractionModuleBase<SocketInteractionContext>
{
    private async Task RespondOrFollowupAsync(
        string? text = null,
        bool ephemeral = false,
        MessageComponent? components = null,
        AllowedMentions? allowedMentions = null)
    {
        var flags = components is not null ? MessageFlags.ComponentsV2 : MessageFlags.None;
        var content = components is not null ? null : text;

        if (Context.Interaction.HasResponded)
            await FollowupAsync(content, ephemeral: ephemeral, components: components,
                allowedMentions: allowedMentions ?? AllowedMentions.None, flags: flags).ConfigureAwait(false);
        else
            await RespondAsync(content, ephemeral: ephemeral, components: components,
                allowedMentions: allowedMentions ?? AllowedMentions.None, flags: flags).ConfigureAwait(false);
    }

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

            var customId = $"assistant:play_search:{Context.User.Id}:{track.Uri?.ToString() ?? string.Empty}";
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
            .WithButton("Cancel", $"assistant:play_search_cancel:{Context.User.Id}", ButtonStyle.Danger));

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

        if (Context.User is not IGuildUser guildUser)
        {
            await RespondOrFollowupAsync("You must be in a guild to use this command.", true).ConfigureAwait(false);
            return;
        }

        if (Context.Channel is not ITextChannel textChannel)
        {
            await RespondOrFollowupAsync("This command must be used in a text channel.", true).ConfigureAwait(false);
            return;
        }

        var connectToVoice = !string.IsNullOrWhiteSpace(query);
        var (player, retrieveStatus) = await musicService.GetPlayerAsync(
            Context.Guild.Id,
            guildUser.VoiceChannel?.Id,
            textChannel,
            connectToVoice ? PlayerChannelBehavior.Join : PlayerChannelBehavior.None,
            connectToVoice ? MemberVoiceStateBehavior.RequireSame : MemberVoiceStateBehavior.Ignore
        ).ConfigureAwait(false);

        if (player is null)
        {
            var errorMessage = MusicModuleHelpers.GetPlayerRetrieveErrorMessage(retrieveStatus);
            await RespondOrFollowupAsync(errorMessage, true).ConfigureAwait(false);
            return;
        }

        if (string.IsNullOrWhiteSpace(query)) // No query means pause/resume
        {
            var (success, message) = await musicService.PauseOrResumeAsync(player, Context.User).ConfigureAwait(false);
            await RespondOrFollowupAsync(message, !success).ConfigureAwait(false);
            return;
        }

        var loadResult = await musicService.LoadAndQueueTrackAsync(player, query, Context.User).ConfigureAwait(false);

        switch (loadResult.Status)
        {
            case TrackLoadStatus.TrackLoaded:
                await RespondOrFollowupAsync(
                        $"Added to queue: {loadResult.LoadedTrack!.Title.AsMarkdownLink(loadResult.LoadedTrack.Uri?.ToString())}")
                    .ConfigureAwait(false);
                await musicService.StartPlaybackIfNeededAsync(player).ConfigureAwait(false);
                break;
            case TrackLoadStatus.PlaylistLoaded:
                await RespondOrFollowupAsync(
                        $"Added {loadResult.Tracks.Count} tracks from playlist '{loadResult.PlaylistInformation!.Name.AsMarkdownLink(loadResult.OriginalQuery)}' to queue.")
                    .ConfigureAwait(false);
                await musicService.StartPlaybackIfNeededAsync(player).ConfigureAwait(false);
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

    [ComponentInteraction("assistant:play_search:*:*", true)]
    public async Task HandleSearchResultSelection(ulong requesterId, string uri)
    {
        if (Context.User.Id != requesterId)
        {
            await RespondAsync("Only the person who started the search can select a track.", ephemeral: true)
                .ConfigureAwait(false);
            return;
        }

        await DeferAsync().ConfigureAwait(false);

        if (Context.User is not IGuildUser guildUser || Context.Channel is not ITextChannel textChannel)
        {
            await FollowupAsync("Error: Could not verify your context.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var (player, retrieveStatus) = await musicService.GetPlayerAsync(
            Context.Guild.Id,
            guildUser.VoiceChannel?.Id,
            textChannel,
            PlayerChannelBehavior.Join,
            MemberVoiceStateBehavior.RequireSame
        ).ConfigureAwait(false);

        if (player is null)
        {
            var errorMessage = MusicModuleHelpers.GetPlayerRetrieveErrorMessage(retrieveStatus);
            await FollowupAsync(errorMessage, ephemeral: true).ConfigureAwait(false);
            return;
        }

        var track = await musicService.GetTrackFromSearchSelectionAsync(uri).ConfigureAwait(false);

        if (track is not null)
        {
            await player.Queue.AddAsync(new TrackQueueItem(track)).ConfigureAwait(false);

            var confirmationContainer = new ContainerBuilder()
                .WithTextDisplay(
                    new TextDisplayBuilder(
                        $"✅ **Added to queue:** {track.Title.AsMarkdownLink(track.Uri?.ToString())}"));

            var components = new ComponentBuilderV2().WithContainer(confirmationContainer).Build();

            await ModifyOriginalResponseAsync(props => { props.Components = components; }).ConfigureAwait(false);

            await musicService.StartPlaybackIfNeededAsync(player).ConfigureAwait(false);
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

    [ComponentInteraction("assistant:play_search_cancel:*", true)]
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