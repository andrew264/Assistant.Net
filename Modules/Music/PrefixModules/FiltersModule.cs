using Assistant.Net.Modules.Music.Base;
using Assistant.Net.Modules.Music.Logic.Player;
using Assistant.Net.Services.Music;
using Assistant.Net.Utilities.Filters;
using Discord;
using Discord.Commands;
using Lavalink4NET.Clients;
using Lavalink4NET.Filters;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Music.PrefixModules;

[Group("filters")]
[Alias("filter", "fx")]
[Summary("Change the audio filters of the player.")]
[RequireContext(ContextType.Guild)]
public class FiltersModule(MusicService musicService, ILogger<FiltersModule> logger)
    : MusicPrefixModuleBase(musicService, logger)
{
    private const float InitialTimescaleStep = 0.1f;

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

    [Command("nightcore")]
    [Alias("nc")]
    [Summary("Enable/Disable the nightcore filter.")]
    public async Task NightcoreAsync()
    {
        var (player, isError) = await GetPlayerForFilterCommandAsync().ConfigureAwait(false);
        if (isError || player is null) return;

        var isCurrentlyEnabled = FilterOperations.IsNightcoreActive(player.Filters.Timescale);
        var enable = !isCurrentlyEnabled;

        player.Filters.Timescale = FilterOperations.GetNightcoreSettings(enable);

        var components = FilterUiBuilder.BuildNightcoreConfirmation(enable);
        logger.LogInformation("[FILTERS CMD] User {User} {Action} nightcore in {GuildName}", Context.User.Username,
            enable ? "enabled" : "disabled", Context.Guild.Name);

        await player.Filters.CommitAsync().ConfigureAwait(false);
        await ReplyAsync(components: components, allowedMentions: AllowedMentions.None,
            flags: MessageFlags.ComponentsV2).ConfigureAwait(false);
    }

    [Command("vaporwave")]
    [Alias("vw", "vapor")]
    [Summary("Enable/Disable the vaporwave filter.")]
    public async Task VaporwaveAsync()
    {
        var (player, isError) = await GetPlayerForFilterCommandAsync().ConfigureAwait(false);
        if (isError || player is null) return;

        var isCurrentlyEnabled = FilterOperations.IsVaporwaveActive(player.Filters.Timescale, player.Filters.Tremolo,
            player.Filters.Equalizer?.Equalizer);
        var enable = !isCurrentlyEnabled;

        var (tsSettings, tremoloSettings, eqSettings) =
            FilterOperations.GetVaporwaveSettings(enable, player.Filters.Equalizer?.Equalizer ?? new Equalizer());

        player.Filters.Timescale = tsSettings;
        player.Filters.Tremolo = tremoloSettings;
        player.Filters.Equalizer = new EqualizerFilterOptions(eqSettings);

        var components = FilterUiBuilder.BuildVaporwaveConfirmation(enable);
        logger.LogInformation("[FILTERS CMD] User {User} {Action} vaporwave in {GuildName}", Context.User.Username,
            enable ? "enabled" : "disabled", Context.Guild.Name);

        await player.Filters.CommitAsync().ConfigureAwait(false);
        await ReplyAsync(components: components, allowedMentions: AllowedMentions.None,
            flags: MessageFlags.ComponentsV2).ConfigureAwait(false);
    }

    [Command("8d")]
    [Summary("Enable/Disable the 8D audio filter.")]
    public async Task EightDAsync()
    {
        var (player, isError) = await GetPlayerForFilterCommandAsync().ConfigureAwait(false);
        if (isError || player is null) return;

        var isCurrentlyEnabled = FilterOperations.Is8DActive(player.Filters.Rotation);
        var enable = !isCurrentlyEnabled;

        player.Filters.Rotation = FilterOperations.Get8DSettings(enable);

        var components = FilterUiBuilder.Build8DConfirmation(enable);
        logger.LogInformation("[FILTERS CMD] User {User} {Action} 8D audio in {GuildName}", Context.User.Username,
            enable ? "enabled" : "disabled", Context.Guild.Name);

        await player.Filters.CommitAsync().ConfigureAwait(false);
        await ReplyAsync(components: components, allowedMentions: AllowedMentions.None,
            flags: MessageFlags.ComponentsV2).ConfigureAwait(false);
    }

    [Command("reset")]
    [Alias("clearfilters", "nofx")]
    [Summary("Reset all audio filters to default.")]
    public async Task ResetFiltersAsync()
    {
        var (player, isError) = await GetPlayerForFilterCommandAsync().ConfigureAwait(false);
        if (isError || player is null) return;

        player.Filters.Clear();
        var components = FilterUiBuilder.BuildResetConfirmation();
        logger.LogInformation("[FILTERS CMD] User {User} reset all filters in {GuildName}", Context.User.Username,
            Context.Guild.Name);

        await player.Filters.CommitAsync().ConfigureAwait(false);
        await ReplyAsync(components: components, allowedMentions: AllowedMentions.None,
            flags: MessageFlags.ComponentsV2).ConfigureAwait(false);
    }

    [Command("bassboost")]
    [Alias("bass", "bb")]
    [Summary("Adjust the bass boost filter.")]
    public async Task BassBoostAsync()
    {
        var (player, isError) = await GetPlayerForFilterCommandAsync().ConfigureAwait(false);
        if (isError || player is null) return;

        var components = FilterUiBuilder.BuildBassBoostDisplay(player, Context.User.Id);
        var msg = await ReplyAsync(components: components, allowedMentions: AllowedMentions.None,
            flags: MessageFlags.ComponentsV2).ConfigureAwait(false);

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

    [Command("trebleboost")]
    [Alias("treble", "tb")]
    [Summary("Adjust the treble boost filter.")]
    public async Task TrebleBoostAsync()
    {
        var (player, isError) = await GetPlayerForFilterCommandAsync().ConfigureAwait(false);
        if (isError || player is null) return;

        var components = FilterUiBuilder.BuildTrebleBoostDisplay(player, Context.User.Id);
        var msg = await ReplyAsync(components: components, allowedMentions: AllowedMentions.None,
            flags: MessageFlags.ComponentsV2).ConfigureAwait(false);

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

    [Command("timescale")]
    [Alias("speed", "pitch", "rate", "ts")]
    [Summary("Adjust playback speed, pitch, and rate.")]
    public async Task TimescaleAsync()
    {
        var (player, isError) = await GetPlayerForFilterCommandAsync().ConfigureAwait(false);
        if (isError || player is null) return;

        var components = FilterUiBuilder.BuildTimescaleDisplay(player, Context.User.Id, InitialTimescaleStep);
        var msg = await ReplyAsync(components: components, allowedMentions: AllowedMentions.None,
            flags: MessageFlags.ComponentsV2).ConfigureAwait(false);

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