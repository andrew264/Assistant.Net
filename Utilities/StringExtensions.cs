namespace Assistant.Net.Utilities;

public static class StringExtensions
{
    public static string CapitalizeFirstLetter(this string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;
        if (input.Length == 1)
            return input.ToUpper();
        return char.ToUpper(input[0]) + input[1..];
    }
}