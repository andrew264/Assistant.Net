using System.Text;
using Assistant.Net.Modules.Games.Autocomplete;
using Assistant.Net.Services.Games;
using Assistant.Net.Utilities;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Assistant.Net.Modules.Games.InteractionModules;

[Group("gamestats", "Commands for viewing game statistics.")]
public class GameStatsModule(
    GameStatsService gameStatsService,
    ILogger<GameStatsModule> logger,
    DiscordSocketClient client)
    : InteractionModuleBase<SocketInteractionContext>
{
    private const int ResultsPerPage = 10;
    private const string TrophyEmojiUrl = "https://em-content.zobj.net/source/twitter/376/trophy_1f3c6.png";

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

            var container = new ContainerBuilder()
                .WithAccentColor(Color.Gold)
                .WithSection(section =>
                {
                    section.AddComponent(
                        new TextDisplayBuilder($"# 🏆 {gameName.CapitalizeFirstLetter()} Leaderboard"));
                    section.AddComponent(new TextDisplayBuilder($"Top players in {Context.Guild.Name}"));
                    section.WithAccessory(new ThumbnailBuilder
                        { Media = new UnfurledMediaItemProperties { Url = TrophyEmojiUrl } });
                })
                .WithSeparator();

            for (var i = 0; i < leaderboardData.Count; i++)
            {
                var entry = leaderboardData[i];
                var user = await client.Rest.GetUserAsync(entry.Id.UserId).ConfigureAwait(false);
                var userName = user?.ToString() ?? $"User ID: {entry.Id.UserId}";
                var userAvatarUrl = user?.GetDisplayAvatarUrl() ?? user?.GetDefaultAvatarUrl();

                if (entry.Games.TryGetValue(gameName, out var stats))
                {
                    var userSection = new SectionBuilder()
                        .AddComponent(new TextDisplayBuilder($"**{i + 1}.** {userName}"))
                        .AddComponent(new TextDisplayBuilder(
                            $"**Elo:** {stats.Elo:F1} (W:{stats.Wins} L:{stats.Losses} T:{stats.Ties})"));

                    if (userAvatarUrl != null)
                        userSection.WithAccessory(new ThumbnailBuilder
                            { Media = new UnfurledMediaItemProperties { Url = userAvatarUrl } });

                    container.WithSection(userSection);
                }
                else
                {
                    logger.LogWarning(
                        "Leaderboard entry for User {UserId} in Guild {GuildId} for Game {GameName} missing projected stats.",
                        entry.Id.UserId, guildId, gameName);
                }
            }

            container
                .WithSeparator()
                .WithTextDisplay(new TextDisplayBuilder(
                    $"*Generated {TimestampTag.FromDateTime(DateTime.UtcNow, TimestampTagStyles.Relative)}*"));

            var components = new ComponentBuilderV2().WithContainer(container).Build();
            await FollowupAsync(components: components, flags: MessageFlags.ComponentsV2).ConfigureAwait(false);
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
            var container = new ContainerBuilder()
                .WithAccentColor(userColor)
                .WithSection(section =>
                {
                    section.AddComponent(new TextDisplayBuilder($"# Game Stats for {targetUser.Username}"));
                    section.AddComponent(new TextDisplayBuilder($"in {Context.Guild.Name}"));
                    section.WithAccessory(new ThumbnailBuilder
                    {
                        Media = new UnfurledMediaItemProperties
                            { Url = targetUser.GetDisplayAvatarUrl() ?? targetUser.GetDefaultAvatarUrl() }
                    });
                });

            var hasStatsToShow = false;

            if (!string.IsNullOrEmpty(gameName))
            {
                if (userStatsData.Games.TryGetValue(gameName, out var stats))
                {
                    var matches = stats.MatchesPlayed;
                    var winRate = matches > 0 ? (double)stats.Wins / matches * 100 : 0;

                    var statsText = new StringBuilder()
                        .AppendLine($"**Elo:** {stats.Elo:F1}")
                        .AppendLine($"**Record:** {stats.Wins} Wins / {stats.Losses} Losses / {stats.Ties} Ties")
                        .AppendLine($"**Total Matches:** {matches}")
                        .AppendLine($"**Win Rate:** {winRate:F1}%")
                        .ToString();

                    container
                        .WithSeparator()
                        .WithTextDisplay(new TextDisplayBuilder($"## {gameName.CapitalizeFirstLetter()}"))
                        .WithTextDisplay(new TextDisplayBuilder(statsText));
                    hasStatsToShow = true;
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
                    .OrderBy(kvp => kvp.Key)
                    .ToList();

                if (gamesPlayed.Count > 0)
                {
                    container.WithSeparator();
                    hasStatsToShow = true;
                }

                foreach (var (gName, gStats) in gamesPlayed)
                {
                    var matches = gStats.MatchesPlayed;
                    var winRate = matches > 0 ? (double)gStats.Wins / matches * 100 : 0;
                    var statsSummary =
                        $"Elo: {gStats.Elo:F1} | W/L/T: {gStats.Wins}/{gStats.Losses}/{gStats.Ties} | Matches: {matches} ({winRate:F1}%)";

                    container.WithSection(section =>
                    {
                        section.AddComponent(new TextDisplayBuilder($"**{gName.CapitalizeFirstLetter()}**"));
                        section.AddComponent(new TextDisplayBuilder(statsSummary));
                    });
                }
            }

            if (!hasStatsToShow)
            {
                await FollowupAsync($"{targetUser.Mention} hasn't played any recorded games in this server yet.")
                    .ConfigureAwait(false);
                return;
            }

            container
                .WithSeparator()
                .WithTextDisplay(new TextDisplayBuilder(
                    $"*Stats as of {TimestampTag.FromDateTime(DateTime.UtcNow, TimestampTagStyles.Relative)}*"));

            var components = new ComponentBuilderV2().WithContainer(container).Build();
            await FollowupAsync(components: components, flags: MessageFlags.ComponentsV2).ConfigureAwait(false);
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