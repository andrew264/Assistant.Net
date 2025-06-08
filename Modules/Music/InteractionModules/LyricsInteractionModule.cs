using System.Globalization;
using Assistant.Net.Models.Lyrics;
using Assistant.Net.Services.Music;
using Assistant.Net.Utilities;
using Discord;
using Discord.Interactions;
using Lavalink4NET.Clients;
using Lavalink4NET.Players;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Music.InteractionModules;

[CommandContextType(InteractionContextType.Guild)]
public class LyricsInteractionModule(
    MusicService musicService,
    GeniusLyricsService geniusLyricsService,
    ILogger<LyricsInteractionModule> logger)
    : InteractionModuleBase<SocketInteractionContext>
{
    private const string LyricsPageButtonPrefix = "assistant:lyrics_page";

    [SlashCommand("lyrics", "Fetch lyrics for a song.")]
    public async Task GetLyricsAsync(
        [Summary("query", "Song title or URL. Leave empty for the current song.")]
        string? query = null)
    {
        await DeferAsync().ConfigureAwait(false);

        string? searchTitle;
        string? searchArtist = null;

        if (string.IsNullOrWhiteSpace(query))
        {
            var (player, _) = await musicService.GetPlayerForContextAsync(
                Context.Guild,
                Context.User,
                Context.Channel,
                PlayerChannelBehavior.None,
                MemberVoiceStateBehavior.Ignore).ConfigureAwait(false);

            if (player?.CurrentTrack != null)
            {
                searchTitle = player.CurrentTrack.Title.RemoveStuffInBrackets();
            }
            else
            {
                var spotifyActivity = Context.User.Activities.OfType<SpotifyGame>().FirstOrDefault();
                if (spotifyActivity != null)
                {
                    searchTitle = spotifyActivity.TrackTitle;
                    searchArtist = spotifyActivity.Artists.First();
                    logger.LogInformation(
                        "Lyrics: No bot track, using User {UserId}'s Spotify: {TrackTitle} by {TrackArtist}",
                        Context.User.Id, searchTitle, searchArtist);
                }
                else
                {
                    await FollowupAsync(
                        "I am not playing anything right now, and you don't seem to be listening to Spotify.",
                        ephemeral: true).ConfigureAwait(false);
                    return;
                }
            }
        }
        else
        {
            searchTitle = query;
        }

        logger.LogInformation("Searching lyrics for Title: '{SearchTitle}', Artist: '{SearchArtist}'", searchTitle,
            searchArtist ?? "N/A");

        var geniusSongs = await geniusLyricsService.SearchSongsAsync(searchTitle, searchArtist).ConfigureAwait(false);

        if (geniusSongs == null || geniusSongs.Count == 0)
        {
            await FollowupAsync($"Sorry, I couldn't find lyrics for '{searchTitle}'.", ephemeral: true)
                .ConfigureAwait(false);
            return;
        }

        var initialBestMatch = geniusSongs.First();
        // Ensure full song details are fetched and cached if not already
        var bestMatch = await geniusLyricsService.GetSongByIdAsync(initialBestMatch.Id).ConfigureAwait(false);

        if (bestMatch == null)
        {
            await FollowupAsync("Could not retrieve full song details from Genius.", ephemeral: true)
                .ConfigureAwait(false);
            return;
        }

        logger.LogInformation(
            "Genius song found for query '{Query}': {FullTitle} by {ArtistNames}, Path: {Path}, ID: {Id}",
            query ?? "(current song)", bestMatch.FullTitle, bestMatch.ArtistNames, bestMatch.Path, bestMatch.Id);
        var lyrics = await geniusLyricsService.GetLyricsFromPathAsync(bestMatch.Id, bestMatch.Path)
            .ConfigureAwait(false);
        if (lyrics == null)
        {
            await FollowupAsync($"Sorry, I couldn't fetch the lyrics.",ephemeral: true).ConfigureAwait(false);
            return;
        }

        var lyricsChunks = lyrics.SmartChunkSplitList();

        var embedColor = Color.Blue;
        if (!string.IsNullOrEmpty(bestMatch.SongArtPrimaryColor))
        {
            var hexColor = bestMatch.SongArtPrimaryColor.Replace("#", "");
            if (uint.TryParse(hexColor, NumberStyles.HexNumber, null, out var colorValue))
                embedColor = new Color(colorValue);
        }

        var (embed, components) = BuildLyricsPage(bestMatch, lyricsChunks, 0, embedColor);
        await FollowupAsync(embed: embed, components: components, ephemeral: false).ConfigureAwait(false);
    }

    private static (Embed Embed, MessageComponent? Components) BuildLyricsPage(GeniusSong song,
        List<string> lyricsChunks,
        int currentPage, Color embedColor)
    {
        var totalPages = lyricsChunks.Count;
        currentPage = Math.Clamp(currentPage, 0, totalPages - 1);

        var embed = new EmbedBuilder()
            .WithTitle($"🎶 {song.FullTitle.Truncate(250)}")
            .WithDescription(lyricsChunks.ElementAtOrDefault(currentPage) ?? "Lyrics page out of bounds.")
            .WithUrl(song.Url)
            .WithColor(embedColor)
            .WithTimestamp(DateTimeOffset.UtcNow);

        if (!string.IsNullOrEmpty(song.SongArtImageThumbnailUrl))
            embed.WithThumbnailUrl(song.SongArtImageThumbnailUrl);

        if (totalPages > 1) embed.WithFooter($"Page {currentPage + 1}/{totalPages}");

        MessageComponent? components = null;
        if (totalPages <= 1) return (embed.Build(), components);
        var cb = new ComponentBuilder()
            .WithButton("Previous", $"{LyricsPageButtonPrefix}:{song.Id}:{currentPage}:prev", ButtonStyle.Secondary,
                disabled: currentPage == 0)
            .WithButton("Next", $"{LyricsPageButtonPrefix}:{song.Id}:{currentPage}:next", ButtonStyle.Secondary,
                disabled: currentPage == totalPages - 1);
        components = cb.Build();

        return (embed.Build(), components);
    }

    [ComponentInteraction(LyricsPageButtonPrefix + ":*:*:*", true)]
    public async Task HandleLyricsPageButtonAsync(long songId, int currentPage, string action)
    {
        await DeferAsync().ConfigureAwait(false);

        var song = geniusLyricsService.GetSongFromCache(songId);
        if (song == null)
        {
            await FollowupAsync("Sorry, the song data for these lyrics has expired. Please search again.",
                ephemeral: true).ConfigureAwait(false);
            try
            {
                await ModifyOriginalResponseAsync(props => props.Components = new ComponentBuilder().Build())
                    .ConfigureAwait(false);
            }
            catch
            {
                /* ignored */
            }

            return;
        }

        var lyrics = await geniusLyricsService.GetLyricsFromPathAsync(songId, song.Path, false).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(lyrics))
        {
            await FollowupAsync("Sorry, the lyrics data has expired or could not be retrieved. Please search again.",
                ephemeral: true).ConfigureAwait(false);
            try
            {
                await ModifyOriginalResponseAsync(props => props.Components = new ComponentBuilder().Build())
                    .ConfigureAwait(false);
            }
            catch
            {
                /* ignored */
            }

            return;
        }

        var lyricsChunks = lyrics.SmartChunkSplitList();
        var totalPages = lyricsChunks.Count;
        var newPage = currentPage;

        switch (action)
        {
            case "prev":
                newPage--;
                break;
            case "next":
                newPage++;
                break;
            default:
                await FollowupAsync("Unknown pagination action.", ephemeral: true).ConfigureAwait(false);
                return;
        }

        newPage = Math.Clamp(newPage, 0, totalPages - 1);

        var embedColor = Color.Blue;
        if (!string.IsNullOrEmpty(song.SongArtPrimaryColor))
        {
            var hexColor = song.SongArtPrimaryColor.Replace("#", "");
            if (uint.TryParse(hexColor, NumberStyles.HexNumber, null, out var colorValue))
                embedColor = new Color(colorValue);
        }

        var (updatedEmbed, updatedComponents) = BuildLyricsPage(song, lyricsChunks, newPage, embedColor);

        await ModifyOriginalResponseAsync(props =>
        {
            props.Embed = updatedEmbed;
            props.Components = updatedComponents;
        }).ConfigureAwait(false);
    }
}