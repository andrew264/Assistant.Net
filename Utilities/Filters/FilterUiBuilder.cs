using System.Globalization;
using Assistant.Net.Modules.Music.Player;
using Discord;
using Lavalink4NET.Filters;

namespace Assistant.Net.Utilities.Filters;

public static class FilterUiBuilder
{
    public const string BassBoostCustomIdPrefix = "filters:bb";
    public const string TrebleBoostCustomIdPrefix = "filters:tb";
    public const string TimescaleCustomIdPrefix = "filters:ts";

    // --- EQ Presets ---
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

    // Helper to check if specific bands in the current EQ match a preset's bands,
    // and optionally if other non-bass/non-treble bands are zero.
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


    // --- Simple Confirmation Embeds ---
    public static Embed BuildNightcoreConfirmationEmbed(bool enabled) =>
        new EmbedBuilder()
            .WithTitle(enabled ? "✨ Nightcore Filter Enabled" : "Nightcore Filter Disabled")
            .WithDescription(enabled ? "Playback speed and pitch increased!" : "Nightcore effect removed.")
            .WithColor(enabled ? Color.Teal : Color.DarkGrey)
            .Build();

    public static Embed BuildVaporwaveConfirmationEmbed(bool enabled) =>
        new EmbedBuilder()
            .WithTitle(enabled ? "🌴 Vaporwave Filter Enabled" : "Vaporwave Filter Disabled")
            .WithDescription(enabled
                ? "Slower pitch, reverb, and tremolo activated."
                : "Vaporwave aesthetic deactivated.")
            .WithColor(enabled ? Color.Magenta : Color.DarkGrey)
            .Build();

    public static Embed Build8DConfirmationEmbed(bool enabled) =>
        new EmbedBuilder()
            .WithTitle(enabled ? "🎧 8D Audio Enabled" : "8D Audio Disabled")
            .WithDescription(enabled ? "Enjoy the surround sound experience!" : "8D audio effect turned off.")
            .WithColor(enabled ? Color.Blue : Color.DarkerGrey)
            .Build();

    public static Embed BuildResetConfirmationEmbed() =>
        new EmbedBuilder()
            .WithTitle("🔄 All Filters Reset")
            .WithDescription("All audio effects have been returned to default.")
            .WithColor(Color.Default)
            .Build();

    // --- Interactive UI Builders ---
    public static (Embed Embed, MessageComponent Components) BuildBassBoostDisplay(CustomPlayer player,
        ulong requesterId)
    {
        var activeLevel = DetermineCurrentBassLevel(player.Filters.Equalizer?.Equalizer);

        var embed = new EmbedBuilder()
            .WithTitle($"🔊 Bass Boost (Current: {activeLevel})")
            .WithDescription("Adjust the bass intensity of the music.")
            .WithColor(activeLevel switch
            {
                "Low" => Color.Green,
                "Medium" => Color.Blue,
                "High" => Color.Red,
                "Custom" => Color.Orange,
                _ => Color.DarkGrey
            })
            .Build();

        var components = new ComponentBuilder()
            .WithButton("Off", $"{BassBoostCustomIdPrefix}:off:{requesterId}", ButtonStyle.Secondary,
                disabled: activeLevel == "Off")
            .WithButton("Low", $"{BassBoostCustomIdPrefix}:low:{requesterId}", ButtonStyle.Success,
                disabled: activeLevel == "Low")
            .WithButton("Medium", $"{BassBoostCustomIdPrefix}:medium:{requesterId}", disabled: activeLevel == "Medium")
            .WithButton("High", $"{BassBoostCustomIdPrefix}:high:{requesterId}", ButtonStyle.Danger,
                disabled: activeLevel == "High")
            .Build();

        return (embed, components);
    }

    public static (Embed Embed, MessageComponent Components) BuildTrebleBoostDisplay(CustomPlayer player,
        ulong requesterId)
    {
        var activeLevel = DetermineCurrentTrebleLevel(player.Filters.Equalizer?.Equalizer);

        var embed = new EmbedBuilder()
            .WithTitle($"🎼 Treble Boost (Current: {activeLevel})")
            .WithDescription("Adjust the treble intensity of the music.")
            .WithColor(activeLevel switch
            {
                "Low" => Color.Green,
                "Medium" => Color.Blue,
                "High" => Color.Red,
                "Custom" => Color.Orange,
                _ => Color.DarkGrey
            })
            .Build();

        var components = new ComponentBuilder()
            .WithButton("Off", $"{TrebleBoostCustomIdPrefix}:off:{requesterId}", ButtonStyle.Secondary,
                disabled: activeLevel == "Off")
            .WithButton("Low", $"{TrebleBoostCustomIdPrefix}:low:{requesterId}", ButtonStyle.Success,
                disabled: activeLevel == "Low")
            .WithButton("Medium", $"{TrebleBoostCustomIdPrefix}:medium:{requesterId}",
                disabled: activeLevel == "Medium")
            .WithButton("High", $"{TrebleBoostCustomIdPrefix}:high:{requesterId}", ButtonStyle.Danger,
                disabled: activeLevel == "High")
            .Build();
        return (embed, components);
    }

    public static (Embed Embed, MessageComponent Components) BuildTimescaleDisplay(CustomPlayer player,
        ulong requesterId, float currentButtonStep)
    {
        var tsOptions = player.Filters.Timescale;
        var speed = tsOptions?.Speed.GetValueOrDefault(1.0f) ?? 1.0f;
        var pitch = tsOptions?.Pitch.GetValueOrDefault(1.0f) ?? 1.0f;
        var rate = tsOptions?.Rate.GetValueOrDefault(1.0f) ?? 1.0f;

        var embed = new EmbedBuilder()
            .WithTitle("⏱️ Timescale Controls")
            .WithDescription(
                $"Use the buttons to adjust speed, pitch, and rate.\nAdjustment Step: **{currentButtonStep * 100:F0}%**")
            .WithColor(Color.Orange)
            .AddField("Speed", $"{speed * 100:F0}%", true)
            .AddField("Pitch", $"{pitch * 100:F0}%", true)
            .AddField("Rate", $"{rate * 100:F0}%", true)
            .Build();

        var components = new ComponentBuilder();


        components.WithButton("⬆️ Speed", ButtonId("speed_up"), ButtonStyle.Success, row: 0, disabled: speed >= 1.99f);
        components.WithButton("⬇️ Speed", ButtonId("speed_down"), ButtonStyle.Danger, row: 0, disabled: speed <= 0.51f);
        components.WithButton("Reset Speed", ButtonId("speed_reset"), ButtonStyle.Secondary, row: 0,
            disabled: Math.Abs(speed - 1.0f) < 0.01f);

        components.WithButton("⬆️ Pitch", ButtonId("pitch_up"), ButtonStyle.Success, row: 1, disabled: pitch >= 1.99f);
        components.WithButton("⬇️ Pitch", ButtonId("pitch_down"), ButtonStyle.Danger, row: 1, disabled: pitch <= 0.51f);
        components.WithButton("Reset Pitch", ButtonId("pitch_reset"), ButtonStyle.Secondary, row: 1,
            disabled: Math.Abs(pitch - 1.0f) < 0.01f);

        components.WithButton("⬆️ Rate", ButtonId("rate_up"), ButtonStyle.Success, row: 2, disabled: rate >= 1.99f);
        components.WithButton("⬇️ Rate", ButtonId("rate_down"), ButtonStyle.Danger, row: 2, disabled: rate <= 0.51f);
        components.WithButton("Reset Rate", ButtonId("rate_reset"), ButtonStyle.Secondary, row: 2,
            disabled: Math.Abs(rate - 1.0f) < 0.01f);

        var nextStepLabel = Math.Abs(currentButtonStep - 0.1f) < 0.01f ? "Set Step: 5%" : "Set Step: 10%";
        var nextStepValueForId = Math.Abs(currentButtonStep - 0.1f) < 0.01f ? 0.05f : 0.1f;

        var stepToggleButtonId =
            $"{TimescaleCustomIdPrefix}:step_toggle:{requesterId}:{nextStepValueForId.ToString("0.0#", CultureInfo.InvariantCulture)}";

        components.WithButton(nextStepLabel, stepToggleButtonId, ButtonStyle.Secondary, row: 3);
        components.WithButton("Reset All Timescale", ButtonId("reset_all_timescale"), ButtonStyle.Danger, row: 3);


        return (embed, components.Build());

        string ButtonId(string action) =>
            $"{TimescaleCustomIdPrefix}:{action}:{requesterId}:{currentButtonStep.ToString("0.0#", CultureInfo.InvariantCulture)}";
    }

    public static Equalizer GetBassBoostEqualizer(string level) => level.ToLowerInvariant() switch
    {
        "low" => BassLowPreset,
        "medium" => BassMediumPreset,
        "high" => BassHighPreset,
        _ => EqOffPreset // "off"
    };

    public static Equalizer GetTrebleBoostEqualizer(string level) => level.ToLowerInvariant() switch
    {
        "low" => TrebleLowPreset,
        "medium" => TrebleMediumPreset,
        "high" => TrebleHighPreset,
        _ => EqOffPreset // "off"
    };
}