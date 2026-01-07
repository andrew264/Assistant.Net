using Lavalink4NET.Filters;

namespace Assistant.Net.Utilities.Filters;

public static class FilterOperations
{
    // --- Nightcore ---
    private const float NightcoreSpeed = 1.0f;
    private const float NightcorePitch = 1.0f;
    private const float NightcoreRate = 1.2f;

    // --- Vaporwave ---
    // Vaporwave: timescale(pitch=0.85, rate=0.9), tremolo(depth=0.3, freq=14), eq(band0=0.3, band1=0.3)
    private const float VaporwavePitch = 0.85f;
    private const float VaporwaveRate = 0.9f;
    private const float VaporwaveTremoloDepth = 0.3f;
    private const float VaporwaveTremoloFreq = 14.0f;
    private const float VaporwaveEqBand0 = 0.3f;
    private const float VaporwaveEqBand1 = 0.3f;

    // --- 8D Audio ---
    private const float EightDFrequency = 0.2f;

    public static bool IsNightcoreActive(TimescaleFilterOptions? tsOptions)
    {
        if (tsOptions is null) return false;
        return Math.Abs(tsOptions.Rate.GetValueOrDefault(1.0f) - NightcoreRate) < 0.01f &&
               Math.Abs(tsOptions.Speed.GetValueOrDefault(1.0f) - NightcoreSpeed) < 0.01f &&
               Math.Abs(tsOptions.Pitch.GetValueOrDefault(1.0f) - NightcorePitch) < 0.01f;
    }

    public static TimescaleFilterOptions GetNightcoreSettings(bool enable) =>
        enable
            ? new TimescaleFilterOptions { Speed = NightcoreSpeed, Pitch = NightcorePitch, Rate = NightcoreRate }
            : new TimescaleFilterOptions { Speed = 1.0f, Pitch = 1.0f, Rate = 1.0f }; // Default timescale

    public static bool IsVaporwaveActive(TimescaleFilterOptions? ts, TremoloFilterOptions? tremolo, Equalizer? eq)
    {
        if (ts is null || tremolo is null || eq is null) return false;

        return Math.Abs(ts.Pitch.GetValueOrDefault(1.0f) - VaporwavePitch) < 0.01f &&
               Math.Abs(ts.Rate.GetValueOrDefault(1.0f) - VaporwaveRate) < 0.01f &&
               Math.Abs(tremolo.Depth.GetValueOrDefault(0.0f) - VaporwaveTremoloDepth) < 0.01f &&
               Math.Abs(tremolo.Frequency.GetValueOrDefault(2.0f) - VaporwaveTremoloFreq) < 0.01f &&
               Math.Abs(eq.Band0 - VaporwaveEqBand0) < 0.01f &&
               Math.Abs(eq.Band1 - VaporwaveEqBand1) < 0.01f;
    }

    public static (TimescaleFilterOptions Timescale, TremoloFilterOptions Tremolo, Equalizer ModifiedEq)
        GetVaporwaveSettings(bool enable, Equalizer currentEqSettings)
    {
        var targetTs = enable
            ? new TimescaleFilterOptions { Pitch = VaporwavePitch, Rate = VaporwaveRate }
            : new TimescaleFilterOptions();

        var targetTremolo = enable
            ? new TremoloFilterOptions { Depth = VaporwaveTremoloDepth, Frequency = VaporwaveTremoloFreq }
            : new TremoloFilterOptions();

        var eqBuilder = Equalizer.CreateBuilder(currentEqSettings);
        if (enable)
        {
            eqBuilder.Band0 = VaporwaveEqBand0;
            eqBuilder.Band1 = VaporwaveEqBand1;
        }
        else
        {
            if (Math.Abs(currentEqSettings.Band0 - VaporwaveEqBand0) < 0.01f) eqBuilder.Band0 = 0.0f;
            if (Math.Abs(currentEqSettings.Band1 - VaporwaveEqBand1) < 0.01f) eqBuilder.Band1 = 0.0f;
        }

        var modifiedEq = eqBuilder.Build();

        return (targetTs, targetTremolo, modifiedEq);
    }

    public static bool Is8DActive(RotationFilterOptions? rotOptions)
    {
        if (rotOptions is null) return false;
        return Math.Abs(rotOptions.Frequency.GetValueOrDefault(0.0f) - EightDFrequency) < 0.01f;
    }

    public static RotationFilterOptions Get8DSettings(bool enable) =>
        enable
            ? new RotationFilterOptions { Frequency = EightDFrequency }
            : new RotationFilterOptions();

    // --- EQ Preset Application ---
    public static Equalizer ApplyBassBoostPreset(Equalizer? currentEq, Equalizer bassPreset)
    {
        currentEq ??= new Equalizer();
        var eqBuilder = Equalizer.CreateBuilder(currentEq);

        eqBuilder.Band0 = bassPreset.Band0;
        eqBuilder.Band1 = bassPreset.Band1;

        return eqBuilder.Build();
    }

    public static Equalizer ApplyTrebleBoostPreset(Equalizer? currentEq, Equalizer treblePreset)
    {
        currentEq ??= new Equalizer();
        var eqBuilder = Equalizer.CreateBuilder(currentEq);

        eqBuilder.Band10 = treblePreset.Band10;
        eqBuilder.Band11 = treblePreset.Band11;
        eqBuilder.Band12 = treblePreset.Band12;
        eqBuilder.Band13 = treblePreset.Band13;

        return eqBuilder.Build();
    }
}