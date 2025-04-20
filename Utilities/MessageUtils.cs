using System.Text;

namespace Assistant.Net.Utilities;

public static class MessageUtils
{
    public static List<string> SplitMessage(string message, int maxLength)
    {
        var parts = new List<string>();
        if (string.IsNullOrEmpty(message)) return parts;

        var currentPart = new StringBuilder();
        // Prioritize splitting by paragraph first
        var paragraphs = message.Split(["\n\n"], StringSplitOptions.None);

        for (var i = 0; i < paragraphs.Length; i++)
        {
            var paragraph = paragraphs[i];
            var paragraphWithSeparator = i < paragraphs.Length - 1 ? paragraph + "\n\n" : paragraph;

            if (paragraphWithSeparator.Length > maxLength)
            {
                if (currentPart.Length > 0)
                {
                    parts.Add(currentPart.ToString());
                    currentPart.Clear();
                }

                // Split the oversized paragraph itself
                var startIndex = 0;
                while (startIndex < paragraph.Length)
                {
                    var length = Math.Min(maxLength, paragraph.Length - startIndex);
                    var splitIndex = paragraph.Substring(startIndex, length).LastIndexOf('\n');
                    if (splitIndex > 0 &&
                        length < maxLength)
                        length = splitIndex + 1;

                    parts.Add(paragraph.Substring(startIndex, length));
                    startIndex += length;
                }
            }
            else if (currentPart.Length + paragraphWithSeparator.Length <= maxLength)
            {
                currentPart.Append(paragraphWithSeparator);
            }
            else
            {
                parts.Add(currentPart.ToString());
                currentPart.Clear();
                currentPart.Append(paragraphWithSeparator);
            }
        }

        if (currentPart.Length > 0) parts.Add(currentPart.ToString());
        return parts.Where(p => !string.IsNullOrEmpty(p)).ToList();
    }
}