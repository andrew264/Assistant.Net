namespace Assistant.Net.Utilities;

public static class FormatExtensions
{
    private static readonly string[] ByteSuffixes = ["bytes", "KB", "MB", "GB", "TB", "PB"];

    private static string FormatInternal(long val)
    {
        switch (val)
        {
            case < 0:
                return "-" + FormatInternal(-val);
            case 0:
                return "0 bytes";
        }

        double num = val;
        var index = 0;

        while (num >= 1024.0 && index < ByteSuffixes.Length - 1)
        {
            num /= 1024.0;
            index++;
        }

        return index == 0 ? $"{num:0} {ByteSuffixes[index]}" : $"{num:0.00} {ByteSuffixes[index]}";
    }

    extension(long bytesValue)
    {
        public string ToHumanSize() => FormatInternal(bytesValue);
    }

    extension(int bytesValue)
    {
        public string ToHumanSize() => FormatInternal(bytesValue);
    }
}