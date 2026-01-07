using System.Globalization;
using Assistant.Net.Services.Music.Logic;
using Discord;
using Lavalink4NET.Filters;

namespace Assistant.Net.Utilities.Filters;

public static class FilterUiBuilder
{
    public const string NavMenuId = "filters:nav";
    public const string EqBassPrefix = "filters:eq:bass";
    public const string EqTreblePrefix = "filters:eq:treble";
    public const string EqResetId = "filters:eq:reset";
    public const string TsPrefix = "filters:ts";
    public const string FxPrefix = "filters:fx";

    private static readonly Equalizer EqOffPreset = new();
    private static readonly Equalizer BassLowPreset = new() { Band0 = 0.20f, Band1 = 0.15f };
    private static readonly Equalizer BassMediumPreset = new() { Band0 = 0.40f, Band1 = 0.25f };
    private static readonly Equalizer BassHighPreset = new() { Band0 = 0.60f, Band1 = 0.35f };

    private static readonly Equalizer TrebleLowPreset = new()
        { Band10 = 0.2f, Band11 = 0.2f, Band12 = 0.2f, Band13 = 0.25f };

    private static readonly Equalizer TrebleMediumPreset =
        new() { Band10 = 0.4f, Band11 = 0.4f, Band12 = 0.4f, Band13 = 0.45f };

    private static readonly Equalizer TrebleHighPreset = new()
        { Band10 = 0.6f, Band11 = 0.6f, Band12 = 0.6f, Band13 = 0.65f };

    public static MessageComponent BuildFilterUi(CustomPlayer player, ulong requesterId, string view,
        float tsStep = 0.1f)
    {
        return view switch
        {
            "view_ts" => BuildTimescaleView(player, requesterId, tsStep),
            "view_fx" => BuildEffectsView(player, requesterId),
            _ => BuildEqualizerView(player, requesterId)
        };
    }

    private static ActionRowBuilder CreateNavigationRow(ulong requesterId, string currentView) =>
        new ActionRowBuilder()
            .WithComponents([
                new SelectMenuBuilder()
                    .WithType(ComponentType.SelectMenu)
                    .WithCustomId($"{NavMenuId}:{requesterId}")
                    .WithPlaceholder("Select Audio Category")
                    .WithOptions([
                        new SelectMenuOptionBuilder()
                            .WithLabel("Equalizer")
                            .WithDescription("Bass and Treble adjustments")
                            .WithValue("view_eq")
                            .WithEmote(Emoji.Parse("🎚️"))
                            .WithDefault(currentView == "view_eq"),
                        new SelectMenuOptionBuilder()
                            .WithLabel("Timescale")
                            .WithDescription("Speed, Pitch, and Rate control")
                            .WithValue("view_ts")
                            .WithEmote(Emoji.Parse("⏱️"))
                            .WithDefault(currentView == "view_ts"),
                        new SelectMenuOptionBuilder()
                            .WithLabel("Effects")
                            .WithDescription("Filters like Nightcore, Vaporwave, 8D")
                            .WithValue("view_fx")
                            .WithEmote(Emoji.Parse("✨"))
                            .WithDefault(currentView == "view_fx")
                    ])
            ]);

    private static MessageComponent BuildEqualizerView(CustomPlayer player, ulong requesterId)
    {
        var currentEq = player.Filters.Equalizer?.Equalizer;
        var bassLevel = DetermineCurrentBassLevel(currentEq);
        var trebleLevel = DetermineCurrentTrebleLevel(currentEq);
        var container = new ContainerBuilder();

        container.WithTextDisplay(new TextDisplayBuilder("Filters"));
        container.WithActionRow(CreateNavigationRow(requesterId, "view_eq"));
        container.WithSeparator();

        container.WithTextDisplay(new TextDisplayBuilder($"🔊 Bass Boost: **{bassLevel}**"));
        container.WithActionRow(new ActionRowBuilder()
            .WithComponents([
                BuildEqButton(EqBassPrefix, "off", "Off", bassLevel == "Off", requesterId),
                BuildEqButton(EqBassPrefix, "low", "Low", bassLevel == "Low", requesterId),
                BuildEqButton(EqBassPrefix, "medium", "Medium", bassLevel == "Medium", requesterId),
                BuildEqButton(EqBassPrefix, "high", "High", bassLevel == "High", requesterId)
            ]));

        container.WithSeparator();

        container.WithTextDisplay(new TextDisplayBuilder($"🎼 Treble Boost: **{trebleLevel}**"));
        container.WithActionRow(new ActionRowBuilder()
            .WithComponents([
                BuildEqButton(EqTreblePrefix, "off", "Off", trebleLevel == "Off", requesterId),
                BuildEqButton(EqTreblePrefix, "low", "Low", trebleLevel == "Low", requesterId),
                BuildEqButton(EqTreblePrefix, "medium", "Medium", trebleLevel == "Medium", requesterId),
                BuildEqButton(EqTreblePrefix, "high", "High", trebleLevel == "High", requesterId)
            ]));

        container.WithSeparator();

        container.WithActionRow(new ActionRowBuilder()
            .WithButton("Reset Equalizer", $"{EqResetId}:{requesterId}", ButtonStyle.Danger));

        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    private static ButtonBuilder BuildEqButton(string prefix, string value, string label, bool isActive,
        ulong requesterId) =>
        new ButtonBuilder()
            .WithLabel(label)
            .WithCustomId($"{prefix}:{value}:{requesterId}")
            .WithStyle(isActive ? ButtonStyle.Success : ButtonStyle.Secondary)
            .WithDisabled(isActive);

    private static MessageComponent BuildTimescaleView(CustomPlayer player, ulong requesterId, float currentStep)
    {
        var ts = player.Filters.Timescale;
        var speed = ts?.Speed.GetValueOrDefault(1.0f) ?? 1.0f;
        var pitch = ts?.Pitch.GetValueOrDefault(1.0f) ?? 1.0f;
        var rate = ts?.Rate.GetValueOrDefault(1.0f) ?? 1.0f;

        var container = new ContainerBuilder();

        container.WithTextDisplay(new TextDisplayBuilder("Filters"));
        container.WithActionRow(CreateNavigationRow(requesterId, "view_ts"));
        container.WithSeparator();

        container.WithTextDisplay(
            new TextDisplayBuilder(
                $"`Speed: {speed * 100:F0}%` | `Pitch: {pitch * 100:F0}%` | `Rate: {rate * 100:F0}%`"));

        container.WithSeparator();

        var stepStr = currentStep.ToString("0.0#", CultureInfo.InvariantCulture);

        container.WithActionRow(new ActionRowBuilder()
            .WithButton("Speed ⬇️", $"{TsPrefix}:speed_down:{requesterId}:{stepStr}", ButtonStyle.Danger,
                disabled: speed <= 0.51f)
            .WithButton("Reset", $"{TsPrefix}:speed_reset:{requesterId}:{stepStr}", ButtonStyle.Secondary,
                disabled: Math.Abs(speed - 1.0f) < 0.01f)
            .WithButton("Speed ⬆️", $"{TsPrefix}:speed_up:{requesterId}:{stepStr}", ButtonStyle.Success,
                disabled: speed >= 1.99f)
        );

        container.WithActionRow(new ActionRowBuilder()
            .WithButton("Pitch ⬇️", $"{TsPrefix}:pitch_down:{requesterId}:{stepStr}", ButtonStyle.Danger,
                disabled: pitch <= 0.51f)
            .WithButton("Reset", $"{TsPrefix}:pitch_reset:{requesterId}:{stepStr}", ButtonStyle.Secondary,
                disabled: Math.Abs(pitch - 1.0f) < 0.01f)
            .WithButton("Pitch ⬆️", $"{TsPrefix}:pitch_up:{requesterId}:{stepStr}", ButtonStyle.Success,
                disabled: pitch >= 1.99f)
        );

        container.WithActionRow(new ActionRowBuilder()
            .WithButton("Rate ⬇️", $"{TsPrefix}:rate_down:{requesterId}:{stepStr}", ButtonStyle.Danger,
                disabled: rate <= 0.51f)
            .WithButton("Reset", $"{TsPrefix}:rate_reset:{requesterId}:{stepStr}", ButtonStyle.Secondary,
                disabled: Math.Abs(rate - 1.0f) < 0.01f)
            .WithButton("Rate ⬆️", $"{TsPrefix}:rate_up:{requesterId}:{stepStr}", ButtonStyle.Success,
                disabled: rate >= 1.99f)
        );

        container.WithSeparator();

        var nextStepLabel = Math.Abs(currentStep - 0.1f) < 0.01f ? "Set Step: 5%" : "Set Step: 10%";
        var nextStepValue = Math.Abs(currentStep - 0.1f) < 0.01f ? 0.05f : 0.1f;
        var nextStepStr = nextStepValue.ToString("0.0#", CultureInfo.InvariantCulture);

        container.WithActionRow(new ActionRowBuilder()
            .WithButton(nextStepLabel, $"{TsPrefix}:step_toggle:{requesterId}:{nextStepStr}", ButtonStyle.Secondary)
            .WithButton("Reset Timescale", $"{TsPrefix}:reset_all_timescale:{requesterId}:{stepStr}",
                ButtonStyle.Danger));

        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    private static MessageComponent BuildEffectsView(CustomPlayer player, ulong requesterId)
    {
        var ts = player.Filters.Timescale;
        var tremolo = player.Filters.Tremolo;
        var eq = player.Filters.Equalizer?.Equalizer;
        var rotation = player.Filters.Rotation;

        var isNc = FilterOperations.IsNightcoreActive(ts);
        var isVw = FilterOperations.IsVaporwaveActive(ts, tremolo, eq);
        var is8D = FilterOperations.Is8DActive(rotation);

        var container = new ContainerBuilder();

        container.WithTextDisplay(new TextDisplayBuilder("Filters"));
        container.WithActionRow(CreateNavigationRow(requesterId, "view_fx"));
        container.WithSeparator();

        var activeEffects = new List<string>();
        if (isNc) activeEffects.Add("Nightcore");
        if (isVw) activeEffects.Add("Vaporwave");
        if (is8D) activeEffects.Add("8D Audio");

        var statusText = activeEffects.Count > 0
            ? $"Active: **{string.Join(", ", activeEffects)}**"
            : "No effects currently active.";

        container.WithTextDisplay(new TextDisplayBuilder(statusText));

        container.WithActionRow(new ActionRowBuilder()
            .WithComponents([
                new ButtonBuilder()
                    .WithLabel("Nightcore")
                    .WithCustomId($"{FxPrefix}:toggle:nc:{requesterId}")
                    .WithStyle(isNc ? ButtonStyle.Success : ButtonStyle.Secondary),
                new ButtonBuilder()
                    .WithLabel("Vaporwave")
                    .WithCustomId($"{FxPrefix}:toggle:vw:{requesterId}")
                    .WithStyle(isVw ? ButtonStyle.Success : ButtonStyle.Secondary),
                new ButtonBuilder()
                    .WithLabel("8D Audio")
                    .WithCustomId($"{FxPrefix}:toggle:8d:{requesterId}")
                    .WithStyle(is8D ? ButtonStyle.Success : ButtonStyle.Secondary)
            ]));

        container.WithSeparator();

        container.WithActionRow(new ActionRowBuilder()
            .WithButton("Disable All Effects", $"{FxPrefix}:reset:{requesterId}", ButtonStyle.Danger));

        return new ComponentBuilderV2().WithContainer(container).Build();
    }

    public static Equalizer GetBassBoostEqualizer(string level)
    {
        return level.ToLowerInvariant() switch
        {
            "low" => BassLowPreset,
            "medium" => BassMediumPreset,
            "high" => BassHighPreset,
            _ => EqOffPreset
        };
    }

    public static Equalizer GetTrebleBoostEqualizer(string level)
    {
        return level.ToLowerInvariant() switch
        {
            "low" => TrebleLowPreset,
            "medium" => TrebleMediumPreset,
            "high" => TrebleHighPreset,
            _ => EqOffPreset
        };
    }

    private static bool DoesCurrentEqMatchPresetBands(Equalizer? current, Equalizer presetToCompare,
        IEnumerable<int> bandsToCheck, bool checkOtherBandsZero)
    {
        current ??= EqOffPreset;
        if (bandsToCheck.Any(bandIndex => Math.Abs(current[bandIndex] - presetToCompare[bandIndex]) > 0.01f))
            return false;

        if (!checkOtherBandsZero) return true;
        var bassAndTrebleBands = new HashSet<int> { 0, 1, 10, 11, 12, 13 };
        for (var i = 0; i < Equalizer.Bands; i++)
            if (!bassAndTrebleBands.Contains(i) && Math.Abs(current[i]) > 0.01f)
                return false;

        return true;
    }

    private static string DetermineCurrentBassLevel(Equalizer? currentEq)
    {
        if (DoesCurrentEqMatchPresetBands(currentEq, BassHighPreset, [0, 1], true)) return "High";
        if (DoesCurrentEqMatchPresetBands(currentEq, BassMediumPreset, [0, 1], true)) return "Medium";
        if (DoesCurrentEqMatchPresetBands(currentEq, BassLowPreset, [0, 1], true)) return "Low";
        if (DoesCurrentEqMatchPresetBands(currentEq, EqOffPreset, [0, 1], true)) return "Off";

        if (currentEq != null && (Math.Abs(currentEq.Band0) > 0.01f || Math.Abs(currentEq.Band1) > 0.01f))
            return "Custom";

        return "Off";
    }

    private static string DetermineCurrentTrebleLevel(Equalizer? currentEq)
    {
        if (DoesCurrentEqMatchPresetBands(currentEq, TrebleHighPreset, [10, 11, 12, 13], true)) return "High";
        if (DoesCurrentEqMatchPresetBands(currentEq, TrebleMediumPreset, [10, 11, 12, 13], true)) return "Medium";
        if (DoesCurrentEqMatchPresetBands(currentEq, TrebleLowPreset, [10, 11, 12, 13], true)) return "Low";
        if (DoesCurrentEqMatchPresetBands(currentEq, EqOffPreset, [10, 11, 12, 13], true)) return "Off";

        if (currentEq != null && (Math.Abs(currentEq.Band10) > 0.01f || Math.Abs(currentEq.Band11) > 0.01f ||
                                  Math.Abs(currentEq.Band12) > 0.01f ||
                                  Math.Abs(currentEq.Band13) > 0.01f)) return "Custom";

        return "Off";
    }
}