namespace Assistant.Net.Utilities;

public static class StringExtensions
{
    extension(string input)
    {
        public string CapitalizeFirstLetter()
        {
            if (string.IsNullOrEmpty(input))
                return input;
            if (input.Length == 1)
                return input.ToUpper();
            return char.ToUpper(input[0]) + input[1..];
        }

        public string AsMarkdownLink(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return input;
            var sanitizedText = input.Replace("[", "\\[").Replace("]", "\\]");
            return $"[{sanitizedText}](<{url}>)";
        }

        public string Truncate(int maxLength, string truncationSuffix = "...")
        {
            if (string.IsNullOrEmpty(input) || input.Length <= maxLength) return input;
            return string.Concat(input.AsSpan(0, maxLength - truncationSuffix.Length), truncationSuffix);
        }

        public List<string> SmartChunkSplitList() => StringSplitter.SplitString(input);
        public string RemoveStuffInBrackets() => RegexPatterns.Bracket().Replace(input, "");
    }
}