using System.Text;
using Assistant.Net.Modules.Games.Autocomplete;
using Assistant.Net.Services.Games;
using Assistant.Net.Utilities;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Assistant.Net.Modules.Games;

[Group("gamestats", "Commands for viewing game statistics.")]
public class GameStatsModule(
    GameStatsService gameStatsService,
    ILogger<GameStatsModule> logger,
    DiscordSocketClient client)
    : InteractionModuleBase<SocketInteractionContext>
{
    private const int ResultsPerPage = 10;

    [SlashCommand("leaderboard", "Shows the leaderboard for a game in this server.")]
    [RequireContext(ContextType.Guild)]
    public async Task LeaderboardAsync(
        [Summary("game", "The game to show the leaderboard for.")] [Autocomplete(typeof(GameAutocompleteProvider))]
        string gameName)
    {
        await DeferAsync().ConfigureAwait(false);
        var guildId = Context.Guild.Id;

        if (!GameAutocompleteProvider.GameNames.Contains(gameName, StringComparer.OrdinalIgnoreCase))
        {
            await FollowupAsync($"Invalid game specified: {gameName}", ephemeral: true).ConfigureAwait(false);
            return;
        }

        try
        {
            var leaderboardData = await gameStatsService.GetLeaderboardAsync(guildId, gameName, ResultsPerPage)
                .ConfigureAwait(false);

            if (leaderboardData.Count == 0)
            {
                await FollowupAsync($"No stats found for {gameName.CapitalizeFirstLetter()} in this server yet.")
                    .ConfigureAwait(false);
                return;
            }

            var embed = new EmbedBuilder()
                .WithTitle($"{gameName.CapitalizeFirstLetter()} Leaderboard - {Context.Guild.Name}")
                .WithColor(Color.Gold)
                .WithTimestamp(DateTimeOffset.UtcNow);

            var description = new StringBuilder();
            for (var i = 0; i < leaderboardData.Count; i++)
            {
                var entry = leaderboardData[i];
                var user = await client.Rest.GetUserAsync(entry.Id.UserId).ConfigureAwait(false);
                var userName = user?.ToString() ?? $"User ID: {entry.Id.UserId}";

                if (entry.Games.TryGetValue(gameName, out var stats))
                {
                    var elo = stats.Elo;
                    var wins = stats.Wins;
                    var losses = stats.Losses;
                    var ties = stats.Ties;

                    description.AppendLine(
                        $"**{i + 1}.** {userName} - **Elo:** {elo:F1} (W:{wins} L:{losses} T:{ties})");
                }
                else
                {
                    description.AppendLine($"**{i + 1}.** {userName} - Error loading stats");
                    logger.LogWarning(
                        "Leaderboard entry for User {UserId} in Guild {GuildId} for Game {GameName} missing projected stats.",
                        entry.Id.UserId, guildId, gameName);
                }
            }

            embed.Description = description.ToString();
            await FollowupAsync(embed: embed.Build()).ConfigureAwait(false);
        }
        catch (MongoException ex)
        {
            logger.LogError(ex, "MongoDB error fetching leaderboard for {GameName} in Guild {GuildId}", gameName,
                guildId);
            await FollowupAsync($"A database error occurred while fetching the leaderboard: {ex.Message}")
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching leaderboard for {GameName} in Guild {GuildId}", gameName, guildId);
            await FollowupAsync("An error occurred while fetching the leaderboard.").ConfigureAwait(false);
        }
    }

    [SlashCommand("stats", "Shows game statistics for a user in this server.")]
    [RequireContext(ContextType.Guild)]
    public async Task StatsAsync(
        [Summary("user", "The user to show stats for (defaults to you).")]
        IUser? user = null,
        [Summary("game", "The specific game to show stats for (defaults to all games).")]
        [Autocomplete(typeof(GameAutocompleteProvider))]
        string? gameName = null)
    {
        await DeferAsync().ConfigureAwait(false);
        var guildId = Context.Guild.Id;
        var targetUser = user ?? Context.User;

        // Validate game name if provided
        if (gameName != null &&
            !GameAutocompleteProvider.GameNames.Contains(gameName, StringComparer.OrdinalIgnoreCase))
        {
            await FollowupAsync($"Invalid game specified: {gameName}", ephemeral: true).ConfigureAwait(false);
            return;
        }

        try
        {
            var userStatsData =
                await gameStatsService.GetUserGuildStatsAsync(targetUser.Id, guildId).ConfigureAwait(false);

            if (userStatsData == null || userStatsData.Games.Count == 0)
            {
                await FollowupAsync($"{targetUser.Mention} hasn't played any recorded games in this server yet.")
                    .ConfigureAwait(false);
                return;
            }

            var userColor = UserUtils.GetTopRoleColor(targetUser as SocketUser ?? Context.Guild.GetUser(targetUser.Id));
            var embed = new EmbedBuilder()
                .WithTitle($"Game Stats for {targetUser.Username} in {Context.Guild.Name}")
                .WithColor(userColor == Color.Default ? Color.Blue : userColor)
                .WithThumbnailUrl(targetUser.GetDisplayAvatarUrl() ?? targetUser.GetDefaultAvatarUrl())
                .WithTimestamp(DateTimeOffset.UtcNow);

            if (!string.IsNullOrEmpty(gameName))
            {
                if (userStatsData.Games.TryGetValue(gameName, out var stats))
                {
                    var elo = stats.Elo;
                    var wins = stats.Wins;
                    var losses = stats.Losses;
                    var ties = stats.Ties;
                    var matches = stats.MatchesPlayed;
                    var winRate = matches > 0 ? (double)wins / matches * 100 : 0;

                    embed.AddField(gameName.CapitalizeFirstLetter(),
                        $"**Elo:** {elo:F1}\n" +
                        $"**Wins:** {wins}\n" +
                        $"**Losses:** {losses}\n" +
                        $"**Ties:** {ties}\n" +
                        $"**Matches:** {matches}\n" +
                        $"**Win Rate:** {winRate:F1}%");
                }
                else
                {
                    await FollowupAsync(
                            $"{targetUser.Mention} hasn't played {gameName.CapitalizeFirstLetter()} in this server yet.")
                        .ConfigureAwait(false);
                    return;
                }
            }
            else // Show all games
            {
                var gamesPlayed = userStatsData.Games
                    .Where(kvp => GameAutocompleteProvider.GameNames.Contains(kvp.Key))
                    .ToList();

                if (gamesPlayed.Count == 0)
                {
                    await FollowupAsync($"{targetUser.Mention} hasn't played any recorded games in this server yet.")
                        .ConfigureAwait(false);
                    return;
                }

                foreach (var (gName, gStats) in gamesPlayed.OrderBy(kvp => kvp.Key))
                {
                    var elo = gStats.Elo;
                    var wins = gStats.Wins;
                    var losses = gStats.Losses;
                    var ties = gStats.Ties;
                    var matches = gStats.MatchesPlayed;
                    var winRate = matches > 0 ? (double)wins / matches * 100 : 0;

                    embed.AddField(gName.CapitalizeFirstLetter(),
                        $"**Elo:** {elo:F1}\n" +
                        $"W/L/T: {wins}/{losses}/{ties}\n" +
                        $"Matches: {matches} ({winRate:F1}%)",
                        true);
                }
            }

            if (embed.Fields.Count == 0)
            {
                await FollowupAsync($"{targetUser.Mention} hasn't played any recorded games in this server yet.")
                    .ConfigureAwait(false);
                return;
            }

            await FollowupAsync(embed: embed.Build()).ConfigureAwait(false);
        }
        catch (MongoException ex)
        {
            logger.LogError(ex, "MongoDB error fetching stats for User {UserId} in Guild {GuildId} (Game: {GameName})",
                targetUser.Id, guildId, gameName ?? "All");
            await FollowupAsync($"A database error occurred while fetching stats: {ex.Message}").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching stats for User {UserId} in Guild {GuildId} (Game: {GameName})",
                targetUser.Id, guildId, gameName ?? "All");
            await FollowupAsync("An error occurred while fetching stats.").ConfigureAwait(false);
        }
    }
}