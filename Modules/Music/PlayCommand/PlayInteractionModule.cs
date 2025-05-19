using Assistant.Net.Models.Music;
using Assistant.Net.Modules.Music.Autocomplete;
using Assistant.Net.Modules.Music.Helpers;
using Assistant.Net.Services;
using Assistant.Net.Utilities;
using Discord;
using Discord.Interactions;
using Lavalink4NET.Clients;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.Tracks;

namespace Assistant.Net.Modules.Music.PlayCommand;

[CommandContextType(InteractionContextType.Guild)]
public class PlayInteractionModule(
    MusicService musicService
) : InteractionModuleBase<SocketInteractionContext>
{
    private async Task RespondOrFollowupAsync(
        string? text = null,
        bool ephemeral = false,
        Embed? embed = null,
        MessageComponent? components = null,
        AllowedMentions? allowedMentions = null)
    {
        if (Context.Interaction.HasResponded)
            await FollowupAsync(text, ephemeral: ephemeral, embeds: embed != null ? [embed] : null,
                components: components, allowedMentions: allowedMentions ?? AllowedMentions.None).ConfigureAwait(false);
        else
            await RespondAsync(text, ephemeral: ephemeral, embed: embed, components: components,
                allowedMentions: allowedMentions ?? AllowedMentions.None).ConfigureAwait(false);
    }

    private async Task HandleSearchResultsUi(IReadOnlyList<LavalinkTrack> tracks, string originalQuery)
    {
        var topTracks = tracks.Take(5).ToList();
        if (topTracks.Count == 0)
        {
            await RespondOrFollowupAsync("No search results found.", true).ConfigureAwait(false);
            return;
        }

        var embed = new EmbedBuilder()
            .WithTitle($"🔎 Search Results for: `{originalQuery}`")
            .WithColor(Color.Blue)
            .WithDescription("Select a track to add to the queue:");

        var components = new ComponentBuilder();
        for (var i = 0; i < topTracks.Count; i++)
        {
            var track = topTracks[i];
            var title = track.Title.Truncate(50);
            embed.AddField("\u200B",
                $"{i + 1}: {title.AsMarkdownLink(track.Uri?.ToString())} by {track.Author} ({track.Duration:mm\\:ss})");

            var customId = $"assistant:play_search:{Context.User.Id}:{track.Uri?.ToString() ?? string.Empty}";
            if (customId.Length <= 100 && track.Uri != null) // Ensure URI is not null for button
                components.WithButton((i + 1).ToString(), customId, ButtonStyle.Secondary);
            else if (track.Uri == null)
                embed.Fields[i].Value += "\n*(Cannot be selected: Missing URI)*";
            else
                embed.Fields[i].Value += "\n*(Cannot be selected via button due to URI length)*";
        }

        await RespondOrFollowupAsync(embed: embed.Build(), components: components.Build(), ephemeral: true).ConfigureAwait(false);
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
                    $"Added to queue: {loadResult.LoadedTrack!.Title.AsMarkdownLink(loadResult.LoadedTrack.Uri?.ToString())}").ConfigureAwait(false);
                await musicService.StartPlaybackIfNeededAsync(player).ConfigureAwait(false);
                break;
            case TrackLoadStatus.PlaylistLoaded:
                await RespondOrFollowupAsync(
                    $"Added {loadResult.Tracks.Count} tracks from playlist '{loadResult.PlaylistInformation!.Name.AsMarkdownLink(loadResult.OriginalQuery)}' to queue.").ConfigureAwait(false);
                await musicService.StartPlaybackIfNeededAsync(player).ConfigureAwait(false);
                break;
            case TrackLoadStatus.SearchResults:
                await HandleSearchResultsUi(loadResult.Tracks, loadResult.OriginalQuery).ConfigureAwait(false);
                break;
            case TrackLoadStatus.NoMatches:
                await RespondOrFollowupAsync($"❌ No results found for: `{loadResult.OriginalQuery}`", true).ConfigureAwait(false);
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
            await RespondAsync("Only the person who started the search can select a track.", ephemeral: true).ConfigureAwait(false);
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
            await ModifyOriginalResponseAsync(props =>
            {
                props.Content = $"Added to queue: {track.Title.AsMarkdownLink(track.Uri?.ToString())}";
                props.Embed = null;
                props.Components = new ComponentBuilder().Build();
            }).ConfigureAwait(false);
            await musicService.StartPlaybackIfNeededAsync(player).ConfigureAwait(false);
        }
        else
        {
            await ModifyOriginalResponseAsync(props =>
            {
                props.Content = "❌ Failed to load the selected track.";
                props.Embed = null;
                props.Components = new ComponentBuilder().Build();
            }).ConfigureAwait(false);
        }
    }
}