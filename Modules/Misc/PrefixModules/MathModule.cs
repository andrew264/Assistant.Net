using Assistant.Net.Utilities;
using Discord.Commands;

namespace Assistant.Net.Modules.Misc.PrefixModules;

public class MathModule : ModuleBase<SocketCommandContext>
{
    [Command("calc", RunMode = RunMode.Async)]
    [Alias("math", "calculate", "eval")]
    [Summary("Evaluates a mathematical expression safely.")]
    public async Task CalculateAsync([Remainder] string? expression = null)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            await ReplyAsync("Please provide a mathematical expression. Example: `.calc 2 + 2 * 4`");
            return;
        }

        if (expression.Length > 256)
        {
            await ReplyAsync("Expression is too long.");
            return;
        }

        try
        {
            var result = MathUtils.Evaluate(expression);

            var formattedResult = result % 1 == 0
                ? result.ToString("F0")
                : result.ToString("G15");
            await ReplyAsync($"```\n{formattedResult}\n```");
        }
        catch (DivideByZeroException)
        {
            await ReplyAsync("Cannot divide by zero.");
        }
        catch (ArgumentException ex)
        {
            await ReplyAsync($"Syntax Error: {ex.Message}");
        }
        catch (Exception)
        {
            await ReplyAsync("An error occurred while evaluating the expression.");
        }
    }
}