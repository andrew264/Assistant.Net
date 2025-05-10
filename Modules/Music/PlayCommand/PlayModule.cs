using Assistant.Net.Models.Music;
using Assistant.Net.Services;
using Assistant.Net.Utilities;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Lavalink4NET.Clients;
using Lavalink4NET.Players;
using Lavalink4NET.Tracks;

namespace Assistant.Net.Modules.Music.PlayCommand;

public class PlayModule(
    MusicService musicService
) : ModuleBase<SocketCommandContext>
{
    private async Task HandleSearchResultsUi(IReadOnlyList<LavalinkTrack> tracks, string originalQuery)
    {
        var topTracks = tracks.Take(5).ToList();
        if (topTracks.Count == 0)
        {
            await ReplyAsync("No search results found.");
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
            var title = track.Title.Length > 50 ? track.Title[..47] + "..." : track.Title;
            embed.AddField("\u200B",
                $"{i + 1}: {title.AsMarkdownLink(track.Uri?.ToString())} by {track.Author} ({track.Duration:mm\\:ss})");

            var customId = $"assistant:play_search:{Context.User.Id}:{track.Uri?.ToString() ?? string.Empty}";
            if (customId.Length <= 100 && track.Uri != null)
                components.WithButton((i + 1).ToString(), customId, ButtonStyle.Secondary);
            else if (track.Uri == null)
                embed.Fields[i].Value += "\n*(Cannot be selected: Missing URI)*";
            else
                embed.Fields[i].Value += "\n*(Cannot be selected via button due to URI length)*";
        }

        // Prefix commands typically don't use ephemeral messages for search results easily
        await Context.Channel.SendMessageAsync(embed: embed.Build(), components: components.Build());
    }

    [Command("play", RunMode = RunMode.Async)]
    [Alias("p")]
    [Summary("Plays music, adds to queue, or controls playback.")]
    public async Task PlayAsync([Remainder] string? query = null)
    {
        if (Context.User is not SocketGuildUser guildUser)
        {
            await ReplyAsync("You must be in a guild to use this command.");
            return;
        }

        if (Context.Channel is not ITextChannel textChannel)
        {
            await ReplyAsync("This command must be used in a text channel.");
            return;
        }

        var connectToVoice = !string.IsNullOrWhiteSpace(query);
        var (player, retrieveStatus) = await musicService.GetPlayerAsync(
            Context.Guild.Id,
            guildUser.VoiceChannel?.Id,
            textChannel,
            connectToVoice ? PlayerChannelBehavior.Join : PlayerChannelBehavior.None,
            connectToVoice ? MemberVoiceStateBehavior.RequireSame : MemberVoiceStateBehavior.Ignore
        );

        if (player is null)
        {
            var errorMessage = PlayModuleHelper.GetPlayerRetrieveErrorMessage(retrieveStatus);
            await ReplyAsync(errorMessage);
            return;
        }

        if (string.IsNullOrWhiteSpace(query)) // No query means pause/resume
        {
            var (_, message) = await musicService.PauseOrResumeAsync(player, Context.User);
            await ReplyAsync(message);
            return;
        }

        var loadResult = await musicService.LoadAndQueueTrackAsync(player, query, Context.User);

        switch (loadResult.Status)
        {
            case TrackLoadStatus.TrackLoaded:
                await ReplyAsync(
                    $"Added to queue: {loadResult.LoadedTrack!.Title.AsMarkdownLink(loadResult.LoadedTrack.Uri?.ToString())}");
                await musicService.StartPlaybackIfNeededAsync(player);
                break;
            case TrackLoadStatus.PlaylistLoaded:
                await ReplyAsync(
                    $"Added {loadResult.Tracks.Count} tracks from playlist '{loadResult.PlaylistInformation!.Name.AsMarkdownLink(loadResult.OriginalQuery)}' to queue.");
                await musicService.StartPlaybackIfNeededAsync(player);
                break;
            case TrackLoadStatus.SearchResults:
                await HandleSearchResultsUi(loadResult.Tracks, loadResult.OriginalQuery);
                break;
            case TrackLoadStatus.NoMatches:
                await ReplyAsync($"❌ No results found for: `{loadResult.OriginalQuery}`");
                break;
            case TrackLoadStatus.LoadFailed:
                await ReplyAsync($"❌ Failed to load track(s): {loadResult.ErrorMessage ?? "Unknown error"}");
                break;
        }
    }
}

// Helper class to avoid duplicating error message logic
internal static class PlayModuleHelper
{
    public static string GetPlayerRetrieveErrorMessage(PlayerRetrieveStatus status) => status switch
    {
        PlayerRetrieveStatus.UserNotInVoiceChannel => "You are not connected to a voice channel.",
        PlayerRetrieveStatus.BotNotConnected => "The bot is currently not connected to a voice channel.",
        PlayerRetrieveStatus.VoiceChannelMismatch => "You must be in the same voice channel as the bot.",
        PlayerRetrieveStatus.PreconditionFailed => "The bot is already connected to a different voice channel.",
        _ => "An unknown error occurred while retrieving the player."
    };
}