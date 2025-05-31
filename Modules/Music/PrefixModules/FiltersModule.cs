using Assistant.Net.Modules.Music.Logic;
using Assistant.Net.Modules.Music.Logic.Player;
using Assistant.Net.Services.Music;
using Assistant.Net.Utilities.Filters;
using Discord;
using Discord.Commands;
using Lavalink4NET.Clients;
using Lavalink4NET.Filters;
using Lavalink4NET.Players;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Music.PrefixModules;

[Group("filters")]
[Alias("filter", "fx")]
[Summary("Change the audio filters of the player.")]
[RequireContext(ContextType.Guild)]
public class FiltersModule(MusicService musicService, ILogger<FiltersModule> logger) : ModuleBase<SocketCommandContext>
{
    private const float InitialTimescaleStep = 0.1f;

    private async Task<(CustomPlayer? Player, string? ErrorMessage)> GetPlayerForFilterCommandAsync()
    {
        var (player, retrieveStatus) = await musicService.GetPlayerForContextAsync(
            Context.Guild, Context.User, Context.Channel,
            PlayerChannelBehavior.None,
            MemberVoiceStateBehavior.RequireSame).ConfigureAwait(false);

        if (player is null) return (null, MusicModuleHelpers.GetPlayerRetrieveErrorMessage(retrieveStatus));
        if (player.CurrentTrack is null) return (null, "No music is currently playing to apply filters to.");
        return (player, null);
    }

    [Command("nightcore")]
    [Alias("nc")]
    [Summary("Enable/Disable the nightcore filter.")]
    public async Task NightcoreAsync()
    {
        var (player, errorMessage) = await GetPlayerForFilterCommandAsync().ConfigureAwait(false);
        if (player is null)
        {
            await ReplyAsync(errorMessage ?? "Player not available.", allowedMentions: AllowedMentions.None)
                .ConfigureAwait(false);
            return;
        }

        var isCurrentlyEnabled = FilterOperations.IsNightcoreActive(player.Filters.Timescale);
        var enable = !isCurrentlyEnabled;

        player.Filters.Timescale = FilterOperations.GetNightcoreSettings(enable);

        var embed = FilterUiBuilder.BuildNightcoreConfirmationEmbed(enable);
        logger.LogInformation("[FILTERS CMD] User {User} {Action} nightcore in {GuildName}",
            Context.User.Username, enable ? "enabled" : "disabled", Context.Guild.Name);

        await player.Filters.CommitAsync().ConfigureAwait(false);
        await ReplyAsync(embed: embed, allowedMentions: AllowedMentions.None).ConfigureAwait(false);
    }

    [Command("vaporwave")]
    [Alias("vw", "vapor")]
    [Summary("Enable/Disable the vaporwave filter.")]
    public async Task VaporwaveAsync()
    {
        var (player, errorMessage) = await GetPlayerForFilterCommandAsync().ConfigureAwait(false);
        if (player is null)
        {
            await ReplyAsync(errorMessage ?? "Player not available.", allowedMentions: AllowedMentions.None)
                .ConfigureAwait(false);
            return;
        }

        var isCurrentlyEnabled = FilterOperations.IsVaporwaveActive(
            player.Filters.Timescale,
            player.Filters.Tremolo,
            player.Filters.Equalizer?.Equalizer);
        var enable = !isCurrentlyEnabled;

        var (tsSettings, tremoloSettings, eqSettings) =
            FilterOperations.GetVaporwaveSettings(enable, player.Filters.Equalizer?.Equalizer ?? new Equalizer());

        player.Filters.Timescale = tsSettings;
        player.Filters.Tremolo = tremoloSettings;
        player.Filters.Equalizer = new EqualizerFilterOptions(eqSettings);

        var embed = FilterUiBuilder.BuildVaporwaveConfirmationEmbed(enable);
        logger.LogInformation("[FILTERS CMD] User {User} {Action} vaporwave in {GuildName}",
            Context.User.Username, enable ? "enabled" : "disabled", Context.Guild.Name);

        await player.Filters.CommitAsync().ConfigureAwait(false);
        await ReplyAsync(embed: embed, allowedMentions: AllowedMentions.None).ConfigureAwait(false);
    }

    [Command("8d")]
    [Summary("Enable/Disable the 8D audio filter.")]
    public async Task EightDAsync()
    {
        var (player, errorMessage) = await GetPlayerForFilterCommandAsync().ConfigureAwait(false);
        if (player is null)
        {
            await ReplyAsync(errorMessage ?? "Player not available.", allowedMentions: AllowedMentions.None)
                .ConfigureAwait(false);
            return;
        }

        var isCurrentlyEnabled = FilterOperations.Is8DActive(player.Filters.Rotation);
        var enable = !isCurrentlyEnabled;

        player.Filters.Rotation = FilterOperations.Get8DSettings(enable);

        var embed = FilterUiBuilder.Build8DConfirmationEmbed(enable);
        logger.LogInformation("[FILTERS CMD] User {User} {Action} 8D audio in {GuildName}",
            Context.User.Username, enable ? "enabled" : "disabled", Context.Guild.Name);

        await player.Filters.CommitAsync().ConfigureAwait(false);
        await ReplyAsync(embed: embed, allowedMentions: AllowedMentions.None).ConfigureAwait(false);
    }

    [Command("reset")]
    [Alias("clearfilters", "nofx")]
    [Summary("Reset all audio filters to default.")]
    public async Task ResetFiltersAsync()
    {
        var (player, errorMessage) = await GetPlayerForFilterCommandAsync().ConfigureAwait(false);
        if (player is null)
        {
            await ReplyAsync(errorMessage ?? "Player not available.", allowedMentions: AllowedMentions.None)
                .ConfigureAwait(false);
            return;
        }

        player.Filters.Clear();
        var embed = FilterUiBuilder.BuildResetConfirmationEmbed();
        logger.LogInformation("[FILTERS CMD] User {User} reset all filters in {GuildName}", Context.User.Username,
            Context.Guild.Name);

        await player.Filters.CommitAsync().ConfigureAwait(false);
        await ReplyAsync(embed: embed, allowedMentions: AllowedMentions.None).ConfigureAwait(false);
    }

    [Command("bassboost")]
    [Alias("bass", "bb")]
    [Summary("Adjust the bass boost filter.")]
    public async Task BassBoostAsync()
    {
        var (player, errorMessage) = await GetPlayerForFilterCommandAsync().ConfigureAwait(false);
        if (player is null)
        {
            await ReplyAsync(errorMessage ?? "Player not available.", allowedMentions: AllowedMentions.None)
                .ConfigureAwait(false);
            return;
        }

        var (embed, components) = FilterUiBuilder.BuildBassBoostDisplay(player, Context.User.Id);
        var msg = await ReplyAsync(embed: embed, components: components, allowedMentions: AllowedMentions.None)
            .ConfigureAwait(false);

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
        var (player, errorMessage) = await GetPlayerForFilterCommandAsync().ConfigureAwait(false);
        if (player is null)
        {
            await ReplyAsync(errorMessage ?? "Player not available.", allowedMentions: AllowedMentions.None)
                .ConfigureAwait(false);
            return;
        }

        var (embed, components) = FilterUiBuilder.BuildTrebleBoostDisplay(player, Context.User.Id);
        var msg = await ReplyAsync(embed: embed, components: components, allowedMentions: AllowedMentions.None)
            .ConfigureAwait(false);

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
        var (player, errorMessage) = await GetPlayerForFilterCommandAsync().ConfigureAwait(false);
        if (player is null)
        {
            await ReplyAsync(errorMessage ?? "Player not available.", allowedMentions: AllowedMentions.None)
                .ConfigureAwait(false);
            return;
        }

        var (embed, components) = FilterUiBuilder.BuildTimescaleDisplay(player, Context.User.Id, InitialTimescaleStep);
        var msg = await ReplyAsync(embed: embed, components: components, allowedMentions: AllowedMentions.None)
            .ConfigureAwait(false);

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