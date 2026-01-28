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

[RequireContext(ContextType.Guild)]
public class FiltersInteractionModule(MusicService musicService, ILogger<FiltersInteractionModule> logger)
    : MusicInteractionModuleBase(musicService, logger)
{
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

    [SlashCommand("filter", "Open music filter control panel.")]
    public async Task FilterMenuAsync()
    {
        await DeferAsync().ConfigureAwait(false);
        var (player, isError) = await GetPlayerForFilterCommandAsync().ConfigureAwait(false);
        if (isError || player is null) return;

        var components = FilterUiBuilder.BuildFilterUi(player, Context.User.Id, "view_eq");
        await FollowupAsync(components: components, flags: MessageFlags.ComponentsV2).ConfigureAwait(false);
    }

    [ComponentInteraction(FilterUiBuilder.NavMenuId + ":*", true)]
    public async Task HandleNavigationAsync(ulong requesterId, string[] selections)
    {
        if (Context.User.Id != requesterId)
        {
            await RespondAsync("This menu is not for you.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var selectedView = selections.FirstOrDefault() ?? "view_eq";
        await DeferAsync().ConfigureAwait(false);

        var (player, isError) = await GetPlayerForFilterCommandAsync().ConfigureAwait(false);
        if (isError || player is null) return;

        var components = FilterUiBuilder.BuildFilterUi(player, requesterId, selectedView);
        await ModifyOriginalResponseAsync(props =>
        {
            props.Components = components;
            props.Flags = MessageFlags.ComponentsV2;
        }).ConfigureAwait(false);
    }

    [ComponentInteraction("filters:eq:*:*:*", true)]
    public async Task HandleEqButtonAsync(string type, string level, ulong requesterId)
    {
        if (Context.User.Id != requesterId)
        {
            await RespondAsync("This interaction is not for you.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync().ConfigureAwait(false);
        var (player, isError) = await GetPlayerForFilterCommandAsync().ConfigureAwait(false);
        if (isError || player is null) return;

        switch (type)
        {
            case "bass":
            {
                var targetPreset = FilterUiBuilder.GetBassBoostEqualizer(level);
                var newEq = FilterOperations.ApplyBassBoostPreset(player.Filters.Equalizer?.Equalizer, targetPreset);
                player.Filters.Equalizer = new EqualizerFilterOptions(newEq);
                break;
            }
            case "treble":
            {
                var targetPreset = FilterUiBuilder.GetTrebleBoostEqualizer(level);
                var newEq = FilterOperations.ApplyTrebleBoostPreset(player.Filters.Equalizer?.Equalizer, targetPreset);
                player.Filters.Equalizer = new EqualizerFilterOptions(newEq);
                break;
            }
        }

        await player.Filters.CommitAsync().ConfigureAwait(false);
        var components = FilterUiBuilder.BuildFilterUi(player, requesterId, "view_eq");
        await ModifyOriginalResponseAsync(props =>
        {
            props.Components = components;
            props.Flags = MessageFlags.ComponentsV2;
        }).ConfigureAwait(false);
    }

    [ComponentInteraction(FilterUiBuilder.EqResetId + ":*", true)]
    public async Task HandleEqResetAsync(ulong requesterId)
    {
        if (Context.User.Id != requesterId)
        {
            await RespondAsync("This interaction is not for you.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync().ConfigureAwait(false);
        var (player, isError) = await GetPlayerForFilterCommandAsync().ConfigureAwait(false);
        if (isError || player is null) return;

        player.Filters.Equalizer = null;
        await player.Filters.CommitAsync().ConfigureAwait(false);

        var components = FilterUiBuilder.BuildFilterUi(player, requesterId, "view_eq");
        await ModifyOriginalResponseAsync(props =>
        {
            props.Components = components;
            props.Flags = MessageFlags.ComponentsV2;
        }).ConfigureAwait(false);
    }

    [ComponentInteraction(FilterUiBuilder.TsPrefix + ":*:*:*", true)]
    public async Task HandleTimescaleButtonAsync(string action, ulong requesterId, string stepString)
    {
        if (Context.User.Id != requesterId)
        {
            await RespondAsync("This interaction is not for you.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (!float.TryParse(stepString, NumberStyles.Any, CultureInfo.InvariantCulture, out var buttonStepValue))
        {
            await RespondAsync("Invalid step parameter.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync().ConfigureAwait(false);
        var (player, isError) = await GetPlayerForFilterCommandAsync().ConfigureAwait(false);
        if (isError || player is null) return;

        if (action == "reset_all_timescale")
        {
            player.Filters.Timescale = null;
            await player.Filters.CommitAsync().ConfigureAwait(false);
            var resetComponents = FilterUiBuilder.BuildFilterUi(player, requesterId, "view_ts", buttonStepValue);
            await ModifyOriginalResponseAsync(props =>
            {
                props.Components = resetComponents;
                props.Flags = MessageFlags.ComponentsV2;
            }).ConfigureAwait(false);
            return;
        }

        var currentTsOptions = player.Filters.Timescale ?? new TimescaleFilterOptions();
        var newSpeed = currentTsOptions.Speed.GetValueOrDefault(1.0f);
        var newPitch = currentTsOptions.Pitch.GetValueOrDefault(1.0f);
        var newRate = currentTsOptions.Rate.GetValueOrDefault(1.0f);

        switch (action)
        {
            case "speed_up":
                newSpeed = Math.Min(2.0f, newSpeed + buttonStepValue);
                break;
            case "speed_down":
                newSpeed = Math.Max(0.5f, newSpeed - buttonStepValue);
                break;
            case "speed_reset":
                newSpeed = 1.0f;
                break;

            case "pitch_up":
                newPitch = Math.Min(2.0f, newPitch + buttonStepValue);
                break;
            case "pitch_down":
                newPitch = Math.Max(0.5f, newPitch - buttonStepValue);
                break;
            case "pitch_reset":
                newPitch = 1.0f;
                break;

            case "rate_up":
                newRate = Math.Min(2.0f, newRate + buttonStepValue);
                break;
            case "rate_down":
                newRate = Math.Max(0.5f, newRate - buttonStepValue);
                break;
            case "rate_reset":
                newRate = 1.0f;
                break;

            case "step_toggle":
                break;
        }

        if (action != "step_toggle")
        {
            player.Filters.Timescale = new TimescaleFilterOptions
                { Pitch = newPitch, Rate = newRate, Speed = newSpeed };
            await player.Filters.CommitAsync().ConfigureAwait(false);
        }

        var updatedComponents = FilterUiBuilder.BuildFilterUi(player, requesterId, "view_ts", buttonStepValue);
        await ModifyOriginalResponseAsync(props =>
        {
            props.Components = updatedComponents;
            props.Flags = MessageFlags.ComponentsV2;
        }).ConfigureAwait(false);
    }

    [ComponentInteraction(FilterUiBuilder.FxPrefix + ":toggle:*:*", true)]
    public async Task HandleFxToggleAsync(string effect, ulong requesterId)
    {
        if (Context.User.Id != requesterId)
        {
            await RespondAsync("This interaction is not for you.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync().ConfigureAwait(false);
        var (player, isError) = await GetPlayerForFilterCommandAsync().ConfigureAwait(false);
        if (isError || player is null) return;

        switch (effect)
        {
            case "nc":
                var ncActive = FilterOperations.IsNightcoreActive(player.Filters.Timescale);
                player.Filters.Timescale = FilterOperations.GetNightcoreSettings(!ncActive);
                break;
            case "vw":
                var vwActive = FilterOperations.IsVaporwaveActive(player.Filters.Timescale, player.Filters.Tremolo,
                    player.Filters.Equalizer?.Equalizer);
                var (ts, trem, eq) = FilterOperations.GetVaporwaveSettings(!vwActive,
                    player.Filters.Equalizer?.Equalizer ?? new Equalizer());
                player.Filters.Timescale = ts;
                player.Filters.Tremolo = trem;
                player.Filters.Equalizer = new EqualizerFilterOptions(eq);
                break;
            case "8d":
                var rotActive = FilterOperations.Is8DActive(player.Filters.Rotation);
                player.Filters.Rotation = FilterOperations.Get8DSettings(!rotActive);
                break;
        }

        await player.Filters.CommitAsync().ConfigureAwait(false);
        var components = FilterUiBuilder.BuildFilterUi(player, requesterId, "view_fx");
        await ModifyOriginalResponseAsync(props =>
        {
            props.Components = components;
            props.Flags = MessageFlags.ComponentsV2;
        }).ConfigureAwait(false);
    }

    [ComponentInteraction(FilterUiBuilder.FxPrefix + ":reset:*", true)]
    public async Task HandleFxResetAsync(ulong requesterId)
    {
        if (Context.User.Id != requesterId)
        {
            await RespondAsync("This interaction is not for you.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync().ConfigureAwait(false);
        var (player, isError) = await GetPlayerForFilterCommandAsync().ConfigureAwait(false);
        if (isError || player is null) return;

        player.Filters.Clear();
        await player.Filters.CommitAsync().ConfigureAwait(false);

        var components = FilterUiBuilder.BuildFilterUi(player, requesterId, "view_fx");
        await ModifyOriginalResponseAsync(props =>
        {
            props.Components = components;
            props.Flags = MessageFlags.ComponentsV2;
        }).ConfigureAwait(false);
    }
}