using Assistant.Net.Services.Data;
using Assistant.Net.Utilities.Ui;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Music.Interaction;

public class MusicStatsInteractionModule(
    MusicHistoryService historyService,
    ILogger<MusicStatsInteractionModule> logger)
    : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("wrapped", "View the top played songs for the server or a specific user.")]
    [RequireContext(ContextType.Guild)]
    public async Task WrappedAsync(
        [Summary("user", "The user to fetch stats for. Leave empty for server stats.")]
        IUser? user = null)
    {
        await DeferAsync().ConfigureAwait(false);

        var guildId = Context.Guild.Id;
        var targetUserId = user?.Id;

        string title;
        string? iconUrl;

        if (targetUserId.HasValue)
        {
            title = $"{user!.Username}'s Wrapped";
            iconUrl = user.GetDisplayAvatarUrl() ?? user.GetDefaultAvatarUrl();
        }
        else
        {
            title = $"{Context.Guild.Name} Wrapped";
            iconUrl = Context.Guild.IconUrl;
        }

        try
        {
            var topTracks = await historyService.GetTopPlaysAsync(guildId, targetUserId).ConfigureAwait(false);

            var components = MusicUiFactory.BuildWrappedComponents(
                topTracks,
                1,
                targetUserId ?? 0,
                title,
                iconUrl);

            await FollowupAsync(components: components, flags: MessageFlags.ComponentsV2).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing /wrapped command for Guild {GuildId}", guildId);
            await FollowupAsync("An error occurred while fetching the wrapped stats.", ephemeral: true)
                .ConfigureAwait(false);
        }
    }

    [ComponentInteraction("wrapped:*:*", true)]
    public async Task HandleWrappedPaginationAsync(ulong targetUserIdParam, int page)
    {
        await DeferAsync().ConfigureAwait(false);

        var guildId = Context.Guild.Id;
        ulong? targetUserId = targetUserIdParam == 0 ? null : targetUserIdParam;

        string title;
        string? iconUrl;

        if (targetUserId.HasValue)
        {
            var user = Context.Guild.GetUser(targetUserId.Value) as IUser ??
                       await Context.Client.Rest.GetUserAsync(targetUserId.Value);
            title = user != null ? $"{user.Username}'s Wrapped" : "User Wrapped";
            iconUrl = user?.GetDisplayAvatarUrl() ?? user?.GetDefaultAvatarUrl();
        }
        else
        {
            title = $"{Context.Guild.Name} Wrapped";
            iconUrl = Context.Guild.IconUrl;
        }

        try
        {
            var topTracks = await historyService.GetTopPlaysAsync(guildId, targetUserId).ConfigureAwait(false);

            var components = MusicUiFactory.BuildWrappedComponents(
                topTracks,
                page,
                targetUserId ?? 0,
                title,
                iconUrl);

            await ModifyOriginalResponseAsync(props =>
            {
                props.Components = components;
                props.Flags = MessageFlags.ComponentsV2;
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling wrapped pagination for Guild {GuildId}", guildId);
            await FollowupAsync("An error occurred while updating the stats.", ephemeral: true).ConfigureAwait(false);
        }
    }
}