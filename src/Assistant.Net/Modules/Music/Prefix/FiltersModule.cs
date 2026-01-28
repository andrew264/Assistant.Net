using Assistant.Net.Services.Music;
using Assistant.Net.Services.Music.Logic;
using Assistant.Net.Utilities.Filters;
using Discord;
using Discord.Commands;
using Lavalink4NET.Clients;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Music.Prefix;

[Summary("Open the audio filter control panel.")]
[RequireContext(ContextType.Guild)]
public class FiltersModule(MusicService musicService, ILogger<FiltersModule> logger)
    : MusicPrefixModuleBase(musicService, logger)
{
    private async Task<(CustomPlayer? Player, bool IsError)> GetPlayerForFilterCommandAsync()
    {
        var (player, isError) = await GetVerifiedPlayerAsync(memberBehavior: MemberVoiceStateBehavior.RequireSame)
            .ConfigureAwait(false);
        if (isError || player is null) return (null, true);

        if (player.CurrentTrack is not null) return (player, false);
        await ReplyAsync("No music is currently playing to apply filters to.", allowedMentions: AllowedMentions.None)
            .ConfigureAwait(false);
        return (null, true);
    }

    [Command("filter")]
    [Alias("filters", "fx")]
    [Summary("Open music filter control panel.")]
    public async Task OpenFilterMenuAsync()
    {
        var (player, isError) = await GetPlayerForFilterCommandAsync().ConfigureAwait(false);
        if (isError || player is null) return;

        var components = FilterUiBuilder.BuildFilterUi(player, Context.User.Id, "view_eq");
        var msg = await ReplyAsync(components: components, allowedMentions: AllowedMentions.None,
            flags: MessageFlags.ComponentsV2).ConfigureAwait(false);

        // cleanup after 3 minutes
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMinutes(3)).ConfigureAwait(false);
            try
            {
                await msg.DeleteAsync().ConfigureAwait(false);
            }
            catch
            {
                /* ignored */
            }
        }).ConfigureAwait(false);
    }
}