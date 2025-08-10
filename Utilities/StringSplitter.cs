using Discord;

namespace Assistant.Net.Utilities;

public static class StringSplitter
{
    private const int MaxChunkSize = DiscordConfig.MaxMessageSize;

    public static List<string> SplitString(string input)
    {
        var result = new List<string>();
        var initialChunks = input.Split(["\n\n\n\n"], StringSplitOptions.None);
        foreach (var chunk in initialChunks) ProcessChunk(chunk, result, 0);
        return result;
    }

    private static void ProcessChunk(string chunk, List<string> result, int strategyLevel)
    {
        if (chunk.Length <= MaxChunkSize)
        {
            if (chunk.Length > 0)
                result.Add(chunk);
            return;
        }

        var splitParts = strategyLevel switch
        {
            0 => SplitByMiddleSeparator(chunk, "\n\n"),
            1 => SplitByMiddleSeparatorOr(chunk, "\n\n", "\n"),
            _ => SplitByMiddleWithWordBoundary(chunk)
        };

        foreach (var part in splitParts)
            if (part.Length <= MaxChunkSize)
            {
                if (part.Length > 0)
                    result.Add(part);
            }
            else
            {
                var nextLevel = strategyLevel >= 2 ? 2 : strategyLevel + 1;
                ProcessChunk(part, result, nextLevel);
            }
    }

    private static List<string> SplitByMiddleSeparator(string chunk, string separator)
    {
        var indices = FindAllIndices(chunk, separator);
        if (indices.Count == 0) return [chunk];

        var middleIndex = (indices.Count - 1) / 2;
        var splitPos = indices[middleIndex];
        return SplitAtPosition(chunk, splitPos, separator.Length);
    }

    private static List<string> SplitByMiddleSeparatorOr(string chunk, string primary, string secondary)
    {
        var primaryIndices = FindAllIndices(chunk, primary);
        if (primaryIndices.Count > 0)
        {
            var middleIndex = (primaryIndices.Count - 1) / 2;
            return SplitAtPosition(chunk, primaryIndices[middleIndex], primary.Length);
        }

        var secondaryIndices = FindAllIndices(chunk, secondary);
        if (secondaryIndices.Count == 0) return [chunk];

        var middle = (secondaryIndices.Count - 1) / 2;
        return SplitAtPosition(chunk, secondaryIndices[middle], secondary.Length);
    }

    private static List<string> SplitByMiddleWithWordBoundary(string chunk)
    {
        var middle = chunk.Length / 2;
        var left = middle;
        while (left >= 0 && !char.IsWhiteSpace(chunk[left])) left--;
        var right = middle;
        while (right < chunk.Length && !char.IsWhiteSpace(chunk[right])) right++;

        var splitPos = middle;
        switch (left)
        {
            case >= 0 when right < chunk.Length:
                splitPos = middle - left <= right - middle ? left + 1 : right;
                break;
            case >= 0:
                splitPos = left + 1;
                break;
            default:
            {
                if (right < chunk.Length)
                    splitPos = right;
                break;
            }
        }

        var parts = SplitAtPosition(chunk, splitPos, 0);
        parts[0] = parts[0].TrimEnd();
        parts[1] = parts[1].TrimStart();
        return parts;
    }

    private static List<int> FindAllIndices(string input, string separator)
    {
        var indices = new List<int>();
        var pos = 0;
        while ((pos = input.IndexOf(separator, pos, StringComparison.Ordinal)) != -1)
        {
            indices.Add(pos);
            pos += separator.Length;
        }

        return indices;
    }

    private static List<string> SplitAtPosition(string input, int position, int separatorLength) =>
    [
        input[..position],
        input[(position + separatorLength)..]
    ];
}