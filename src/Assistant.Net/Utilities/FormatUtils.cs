namespace Assistant.Net.Utilities;

public static class FormatUtils
{
    private static readonly string[] ByteSuffixes = ["bytes", "KB", "MB", "GB", "TB", "PB"];

    public static string FormatBytes(long bytesValue)
    {
        switch (bytesValue)
        {
            case < 0:
                return "-" + FormatBytes(-bytesValue);
            case 0:
                return "0.00 bytes";
        }

        double num = bytesValue;

        foreach (var suffix in ByteSuffixes)
        {
            if (num < 1024.0) return suffix == "bytes" ? $"{num:0} {suffix}" : $"{num:0.00} {suffix}";
            num /= 1024.0;
        }

        return "a lot";
    }
}