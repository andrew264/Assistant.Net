using System.Text;

namespace Assistant.Net.Utilities;

public static class MusicUtils
{
    public static string CreateProgressBar(
        TimeSpan currentPosition,
        TimeSpan totalDuration,
        int barLength = 20,
        char progressChar = '⚪',
        char backgroundChar = '─')
    {
        if (barLength <= 0) return string.Empty;
        if (totalDuration <= TimeSpan.Zero) return new string(backgroundChar, barLength);

        var clampedPosition = currentPosition > totalDuration ? totalDuration : currentPosition;
        if (clampedPosition < TimeSpan.Zero) clampedPosition = TimeSpan.Zero;

        var percentage = clampedPosition.TotalSeconds / totalDuration.TotalSeconds;

        var progressMarkerPosition = (int)Math.Round(percentage * barLength);
        progressMarkerPosition = Math.Clamp(progressMarkerPosition, 0, barLength);

        var sb = new StringBuilder(barLength);

        for (var i = 0; i < barLength; i++) sb.Append(i == progressMarkerPosition ? progressChar : backgroundChar);

        return barLength == 1 ? progressChar.ToString() : sb.ToString();
    }
}