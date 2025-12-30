using System.Globalization;
using Assistant.Net.Services.Music;
using Assistant.Net.Services.Music.Logic;
using Assistant.Net.Utilities.Filters;
using Discord;
using Discord.Interactions;
using Lavalink4NET.Clients;
using Lavalink4NET.Filters;
using Microsoft.Extensions.Logging;

namespace Assistant.Net.Modules.Music.Interaction;

[Group("filters", "Change the audio filters of the player.")]
[RequireContext(ContextType.Guild)]
public class FiltersInteractionModule(MusicService musicService, ILogger<FiltersInteractionModule> logger)
    : MusicInteractionModuleBase(musicService, logger)
{
    private const float InitialTimescaleStep = 0.1f;

    private async Task<(CustomPlayer? Player, bool IsError)> GetPlayerForFilterCommandAsync()
    {
        var (player, isError) = await GetVerifiedPlayerAsync(memberBehavior: MemberVoiceStateBehavior.RequireSame)
            .ConfigureAwait(false);
        if (isError || player is null) return (null, true);

        if (player.CurrentTrack is not null) return (player, false);
        await RespondOrFollowupAsync("No music is currently playing to apply filters to.", isError: true)
            .ConfigureAwait(false);
        return (null, true);
    }

    [SlashCommand("nightcore", "Enable/Disable the nightcore filter.")]
    public async Task NightcoreAsync()
    {
        await DeferAsync().ConfigureAwait(false);
        var (player, isError) = await GetPlayerForFilterCommandAsync().ConfigureAwait(false);
        if (isError || player is null) return;

        var isCurrentlyEnabled = FilterOperations.IsNightcoreActive(player.Filters.Timescale);
        var enable = !isCurrentlyEnabled;

        player.Filters.Timescale = FilterOperations.GetNightcoreSettings(enable);

        var components = FilterUiBuilder.BuildNightcoreConfirmation(enable);
        Logger.LogInformation("[FILTERS] User {User} {Action} nightcore in {GuildName}", Context.User.Username,
            enable ? "enabled" : "disabled", Context.Guild.Name);

        await player.Filters.CommitAsync().ConfigureAwait(false);
        await FollowupAsync(components: components, flags: MessageFlags.ComponentsV2).ConfigureAwait(false);
    }

    [SlashCommand("vaporwave", "Enable/Disable the vaporwave filter.")]
    public async Task VaporwaveAsync()
    {
        await DeferAsync().ConfigureAwait(false);
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
        Logger.LogInformation("[FILTERS] User {User} {Action} vaporwave in {GuildName}", Context.User.Username,
            enable ? "enabled" : "disabled", Context.Guild.Name);

        await player.Filters.CommitAsync().ConfigureAwait(false);
        await FollowupAsync(components: components, flags: MessageFlags.ComponentsV2).ConfigureAwait(false);
    }

    [SlashCommand("8d", "Enable/Disable the 8D audio filter.")]
    public async Task EightDAsync()
    {
        await DeferAsync().ConfigureAwait(false);
        var (player, isError) = await GetPlayerForFilterCommandAsync().ConfigureAwait(false);
        if (isError || player is null) return;

        var isCurrentlyEnabled = FilterOperations.Is8DActive(player.Filters.Rotation);
        var enable = !isCurrentlyEnabled;

        player.Filters.Rotation = FilterOperations.Get8DSettings(enable);

        var components = FilterUiBuilder.Build8DConfirmation(enable);
        Logger.LogInformation("[FILTERS] User {User} {Action} 8D audio in {GuildName}", Context.User.Username,
            enable ? "enabled" : "disabled", Context.Guild.Name);

        await player.Filters.CommitAsync().ConfigureAwait(false);
        await FollowupAsync(components: components, flags: MessageFlags.ComponentsV2).ConfigureAwait(false);
    }

    [SlashCommand("reset", "Reset all audio filters to default.")]
    public async Task ResetFiltersAsync()
    {
        await DeferAsync().ConfigureAwait(false);
        var (player, isError) = await GetPlayerForFilterCommandAsync().ConfigureAwait(false);
        if (isError || player is null) return;

        player.Filters.Clear();
        var components = FilterUiBuilder.BuildResetConfirmation();
        Logger.LogInformation("[FILTERS] User {User} reset all filters in {GuildName}", Context.User.Username,
            Context.Guild.Name);

        await player.Filters.CommitAsync().ConfigureAwait(false);
        await FollowupAsync(components: components, flags: MessageFlags.ComponentsV2).ConfigureAwait(false);
    }

    [SlashCommand("bassboost", "Adjust the bass boost filter.")]
    public async Task BassBoostAsync()
    {
        await DeferAsync().ConfigureAwait(false);
        var (player, isError) = await GetPlayerForFilterCommandAsync().ConfigureAwait(false);
        if (isError || player is null) return;

        var components = FilterUiBuilder.BuildBassBoostDisplay(player, Context.User.Id);
        var msg = await FollowupAsync(components: components, flags: MessageFlags.ComponentsV2).ConfigureAwait(false);

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMinutes(3)).ConfigureAwait(false);
            try
            {
                await msg.DeleteAsync().ConfigureAwait(false);
            }
            catch
            {
                // ignored
            }
        }).ConfigureAwait(false);
    }

    [SlashCommand("trebleboost", "Adjust the treble boost filter.")]
    public async Task TrebleBoostAsync()
    {
        await DeferAsync().ConfigureAwait(false);
        var (player, isError) = await GetPlayerForFilterCommandAsync().ConfigureAwait(false);
        if (isError || player is null) return;

        var components = FilterUiBuilder.BuildTrebleBoostDisplay(player, Context.User.Id);
        var msg = await FollowupAsync(components: components, flags: MessageFlags.ComponentsV2).ConfigureAwait(false);

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMinutes(3)).ConfigureAwait(false);
            try
            {
                await msg.DeleteAsync().ConfigureAwait(false);
            }
            catch
            {
                // ignored
            }
        }).ConfigureAwait(false);
    }

    [SlashCommand("timescale", "Adjust playback speed, pitch, and rate.")]
    public async Task TimescaleAsync()
    {
        await DeferAsync().ConfigureAwait(false);
        var (player, isError) = await GetPlayerForFilterCommandAsync().ConfigureAwait(false);
        if (isError || player is null) return;

        var components = FilterUiBuilder.BuildTimescaleDisplay(player, Context.User.Id, InitialTimescaleStep);
        var msg = await FollowupAsync(components: components, flags: MessageFlags.ComponentsV2).ConfigureAwait(false);

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMinutes(3)).ConfigureAwait(false);
            try
            {
                await msg.DeleteAsync().ConfigureAwait(false);
            }
            catch
            {
                // ignored   
            }
        }).ConfigureAwait(false);
    }

    // --- Component Interaction Handlers ---

    [ComponentInteraction("assistant:filters:bb:*:*", true)]
    public async Task HandleBassBoostButtonAsync(string level, ulong originalRequesterId)
    {
        if (Context.User.Id != originalRequesterId)
        {
            await RespondAsync("This interaction is not for you.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync().ConfigureAwait(false);

        var (player, isError) = await GetPlayerForFilterCommandAsync().ConfigureAwait(false);
        if (isError || player is null)
        {
            await ModifyOriginalResponseAsync(props =>
            {
                props.Content = "Player not available or not playing anything.";
                props.Embed = null;
                props.Components = new ComponentBuilder().Build();
            }).ConfigureAwait(false);
            return;
        }

        var targetBassPreset = FilterUiBuilder.GetBassBoostEqualizer(level);
        var newEqSettings =
            FilterOperations.ApplyBassBoostPreset(player.Filters.Equalizer?.Equalizer, targetBassPreset);
        player.Filters.Equalizer = new EqualizerFilterOptions(newEqSettings);

        Logger.LogInformation("[FILTERS] User {User} set BassBoost to {Level} in {GuildName}", Context.User.Username,
            level, Context.Guild.Name);

        var newComponents = FilterUiBuilder.BuildBassBoostDisplay(player, originalRequesterId);
        await ModifyOriginalResponseAsync(props =>
        {
            props.Embed = null;
            props.Components = newComponents;
            props.Flags = MessageFlags.ComponentsV2;
        }).ConfigureAwait(false);
        await player.Filters.CommitAsync().ConfigureAwait(false);
    }

    [ComponentInteraction("assistant:filters:tb:*:*", true)]
    public async Task HandleTrebleBoostButtonAsync(string level, ulong originalRequesterId)
    {
        if (Context.User.Id != originalRequesterId)
        {
            await RespondAsync("This interaction is not for you.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync().ConfigureAwait(false);

        var (player, isError) = await GetPlayerForFilterCommandAsync().ConfigureAwait(false);
        if (isError || player is null)
        {
            await ModifyOriginalResponseAsync(props =>
            {
                props.Content = "Player not available or not playing anything.";
                props.Embed = null;
                props.Components = new ComponentBuilder().Build();
            }).ConfigureAwait(false);
            return;
        }

        var targetTreblePreset = FilterUiBuilder.GetTrebleBoostEqualizer(level);
        var newEqSettings =
            FilterOperations.ApplyTrebleBoostPreset(player.Filters.Equalizer?.Equalizer, targetTreblePreset);
        player.Filters.Equalizer = new EqualizerFilterOptions(newEqSettings);

        Logger.LogInformation("[FILTERS] User {User} set TrebleBoost to {Level} in {GuildName}", Context.User.Username,
            level, Context.Guild.Name);

        var newComponents = FilterUiBuilder.BuildTrebleBoostDisplay(player, originalRequesterId);
        await ModifyOriginalResponseAsync(props =>
        {
            props.Embed = null;
            props.Components = newComponents;
            props.Flags = MessageFlags.ComponentsV2;
        }).ConfigureAwait(false);
        await player.Filters.CommitAsync().ConfigureAwait(false);
    }

    [ComponentInteraction("assistant:filters:ts:*:*:*", true)]
    public async Task HandleTimescaleButtonAsync(string action, ulong originalRequesterId, string stepString)
    {
        if (Context.User.Id != originalRequesterId)
        {
            await RespondAsync("This interaction is not for you.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (!float.TryParse(stepString, NumberStyles.Any, CultureInfo.InvariantCulture, out var buttonStepValue))
        {
            await RespondAsync("Invalid step parameter in button ID. Please try the command again.", ephemeral: true)
                .ConfigureAwait(false);
            Logger.LogError(
                "Failed to parse step '{StepString}' from timescale button ID for user {User}. Button CustomID was likely {FullCustomID}",
                stepString, Context.User.Id,
                Context.Interaction is IComponentInteraction ci ? ci.Data.CustomId : "N/A");
            return;
        }

        await DeferAsync().ConfigureAwait(false);

        var (player, isError) = await GetPlayerForFilterCommandAsync().ConfigureAwait(false);
        if (isError || player is null)
        {
            await ModifyOriginalResponseAsync(props =>
            {
                props.Content = "Player not available or not playing anything.";
                props.Embed = null;
                props.Components = new ComponentBuilder().Build();
            }).ConfigureAwait(false);
            return;
        }

        var currentTsOptions = player.Filters.Timescale ?? new TimescaleFilterOptions();
        var newSpeed = currentTsOptions.Speed.GetValueOrDefault(1.0f);
        var newPitch = currentTsOptions.Pitch.GetValueOrDefault(1.0f);
        var newRate = currentTsOptions.Rate.GetValueOrDefault(1.0f);

        var logMessageAction = action;

        switch (action)
        {
            case "speed_up": newSpeed = Math.Min(2.0f, newSpeed + buttonStepValue); break;
            case "speed_down": newSpeed = Math.Max(0.5f, newSpeed - buttonStepValue); break;
            case "speed_reset": newSpeed = 1.0f; break;

            case "pitch_up": newPitch = Math.Min(2.0f, newPitch + buttonStepValue); break;
            case "pitch_down": newPitch = Math.Max(0.5f, newPitch - buttonStepValue); break;
            case "pitch_reset": newPitch = 1.0f; break;

            case "rate_up": newRate = Math.Min(2.0f, newRate + buttonStepValue); break;
            case "rate_down": newRate = Math.Max(0.5f, newRate - buttonStepValue); break;
            case "rate_reset": newRate = 1.0f; break;

            case "step_toggle":
                logMessageAction = $"step_toggle to {buttonStepValue * 100:F0}%";
                break;
            case "reset_all_timescale":
                player.Filters.Timescale = new TimescaleFilterOptions();
                Logger.LogInformation("[FILTERS] User {User} reset all Timescale in {GuildName}", Context.User.Username,
                    Context.Guild.Name);

                var resetComponents =
                    FilterUiBuilder.BuildTimescaleDisplay(player, originalRequesterId, buttonStepValue);
                await ModifyOriginalResponseAsync(props =>
                {
                    props.Embed = null;
                    props.Components = resetComponents;
                    props.Flags = MessageFlags.ComponentsV2;
                }).ConfigureAwait(false);
                await player.Filters.CommitAsync().ConfigureAwait(false);
                return;
            default:
                Logger.LogWarning("Unknown timescale action: {Action} by {User}", action, Context.User.Username);
                await ModifyOriginalResponseAsync(props => props.Content = "Unknown timescale action.")
                    .ConfigureAwait(false);
                return;
        }

        if (action != "step_toggle")
            player.Filters.Timescale = new TimescaleFilterOptions
                { Pitch = newPitch, Rate = newRate, Speed = newSpeed };

        Logger.LogDebug(
            "[FILTERS] User {User} action '{Action}' (BtnStep {BtnStep}%), Guild {Guild}. New S:{S:F2} P:{P:F2} R:{R:F2}",
            Context.User.Username, logMessageAction, buttonStepValue * 100, Context.Guild.Name, newSpeed, newPitch,
            newRate);

        var updatedComponents = FilterUiBuilder.BuildTimescaleDisplay(player, originalRequesterId, buttonStepValue);
        await ModifyOriginalResponseAsync(props =>
        {
            props.Embed = null;
            props.Components = updatedComponents;
            props.Flags = MessageFlags.ComponentsV2;
        }).ConfigureAwait(false);

        if (action != "step_toggle") await player.Filters.CommitAsync().ConfigureAwait(false);
    }
}