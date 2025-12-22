using System.Globalization;
using System.Text;

namespace Assistant.Net.Utilities;

public static class MathUtils
{
    public static double Evaluate(string expression)
    {
        var tokens = Tokenize(expression);
        var processedTokens = AddImplicitMultiplication(tokens);
        var rpn = ShuntingYard(processedTokens);
        return EvaluateRpn(rpn);
    }

    private static List<string> Tokenize(string expression)
    {
        var tokens = new List<string>();
        var sb = new StringBuilder();

        for (var i = 0; i < expression.Length; i++)
        {
            var c = expression[i];

            if (char.IsWhiteSpace(c)) continue;

            if (char.IsDigit(c) || c == '.')
            {
                sb.Append(c);
                while (i + 1 < expression.Length && (char.IsDigit(expression[i + 1]) || expression[i + 1] == '.'))
                    sb.Append(expression[++i]);
                tokens.Add(sb.ToString());
                sb.Clear();
            }
            else if (char.IsLetter(c))
            {
                sb.Append(c);
                while (i + 1 < expression.Length && char.IsLetter(expression[i + 1])) sb.Append(expression[++i]);
                tokens.Add(sb.ToString().ToLowerInvariant());
                sb.Clear();
            }
            else if (IsOperator(c.ToString()) || c == '(' || c == ')' || c == ',')
            {
                if (c == '-' && (tokens.Count == 0 || IsOperator(tokens.Last()) || tokens.Last() == "(" ||
                                 tokens.Last() == ","))
                {
                    tokens.Add("0");
                    tokens.Add("-");
                }
                else
                {
                    tokens.Add(c.ToString());
                }
            }
            else
            {
                throw new ArgumentException($"Invalid character encountered: {c}");
            }
        }

        return tokens;
    }

    private static List<string> AddImplicitMultiplication(List<string> tokens)
    {
        var result = new List<string>();

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];

            if (i > 0)
            {
                var prev = tokens[i - 1];

                var prevIsOperand = IsNumber(prev) || IsConstant(prev) || (!IsOperator(prev) && !IsFunction(prev) &&
                                                                           prev != "(" && prev != "," && prev != ")");
                var prevIsCloseParen = prev == ")";

                var currIsOperand = IsNumber(token) || IsConstant(token) || (!IsOperator(token) && !IsFunction(token) &&
                                                                             token != "(" && token != "," &&
                                                                             token != ")");
                var currIsOpenParen = token == "(";
                var currIsFunc = IsFunction(token);

                var insert = false;

                if ((prevIsOperand || prevIsCloseParen) && (currIsOpenParen || currIsFunc || currIsOperand))
                    insert = true;

                if (insert) result.Add("*");
            }

            result.Add(token);
        }

        return result;
    }

    private static Queue<string> ShuntingYard(List<string> tokens)
    {
        var outputQueue = new Queue<string>();
        var operatorStack = new Stack<string>();

        foreach (var token in tokens)
            if (IsNumber(token) || IsConstant(token))
            {
                outputQueue.Enqueue(token);
            }
            else if (IsFunction(token))
            {
                operatorStack.Push(token);
            }
            else if (token == ",")
            {
                var foundParen = false;
                while (operatorStack.Count > 0)
                {
                    if (operatorStack.Peek() == "(")
                    {
                        foundParen = true;
                        break;
                    }

                    outputQueue.Enqueue(operatorStack.Pop());
                }

                if (!foundParen) throw new ArgumentException("Misplaced comma or mismatched parentheses.");
            }
            else if (IsOperator(token))
            {
                while (operatorStack.Count > 0 && IsOperator(operatorStack.Peek()))
                {
                    var o1 = token;
                    var o2 = operatorStack.Peek();

                    if ((GetAssociation(o1) == "Left" && GetPrecedence(o1) <= GetPrecedence(o2)) ||
                        (GetAssociation(o1) == "Right" && GetPrecedence(o1) < GetPrecedence(o2)))
                        outputQueue.Enqueue(operatorStack.Pop());
                    else
                        break;
                }

                operatorStack.Push(token);
            }
            else if (token == "(")
            {
                operatorStack.Push(token);
            }
            else if (token == ")")
            {
                var foundOpen = false;
                while (operatorStack.Count > 0)
                {
                    if (operatorStack.Peek() == "(")
                    {
                        foundOpen = true;
                        break;
                    }

                    outputQueue.Enqueue(operatorStack.Pop());
                }

                if (!foundOpen) throw new ArgumentException("Mismatched parentheses: too many closing parentheses.");

                operatorStack.Pop();

                if (operatorStack.Count > 0 && IsFunction(operatorStack.Peek()))
                    outputQueue.Enqueue(operatorStack.Pop());
            }

        while (operatorStack.Count > 0)
        {
            var op = operatorStack.Pop();
            if (op is "(" or ")") throw new ArgumentException("Mismatched parentheses: too many opening parentheses.");
            outputQueue.Enqueue(op);
        }

        return outputQueue;
    }

    private static double EvaluateRpn(Queue<string> rpnQueue)
    {
        var stack = new Stack<double>();

        while (rpnQueue.Count > 0)
        {
            var token = rpnQueue.Dequeue();

            if (double.TryParse(token, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
            {
                stack.Push(number);
            }
            else if (token == "pi")
            {
                stack.Push(Math.PI);
            }
            else if (token == "e")
            {
                stack.Push(Math.E);
            }
            else if (IsOperator(token))
            {
                if (stack.Count < 2)
                    throw new ArgumentException($"Invalid expression: missing operands for operator '{token}'.");
                var right = stack.Pop();
                var left = stack.Pop();

                switch (token)
                {
                    case "+": stack.Push(left + right); break;
                    case "-": stack.Push(left - right); break;
                    case "*": stack.Push(left * right); break;
                    case "/":
                        if (Math.Abs(right) < 1e-15) throw new DivideByZeroException();
                        stack.Push(left / right);
                        break;
                    case "%": stack.Push(left % right); break;
                    case "^": stack.Push(Math.Pow(left, right)); break;
                }
            }
            else if (IsFunction(token))
            {
                if (stack.Count < 1)
                    throw new ArgumentException($"Invalid expression: missing argument for function '{token}'.");
                var val = stack.Pop();
                switch (token.ToLowerInvariant())
                {
                    case "sqrt":
                        if (val < 0) throw new ArgumentException("Cannot calculate square root of a negative number.");
                        stack.Push(Math.Sqrt(val));
                        break;
                    case "sin": stack.Push(Math.Sin(val)); break;
                    case "cos": stack.Push(Math.Cos(val)); break;
                    case "tan": stack.Push(Math.Tan(val)); break;
                    case "abs": stack.Push(Math.Abs(val)); break;
                    case "floor": stack.Push(Math.Floor(val)); break;
                    case "ceil": stack.Push(Math.Ceiling(val)); break;
                    case "round": stack.Push(Math.Round(val)); break;
                    case "log":
                        if (val <= 0) throw new ArgumentException("Logarithm argument must be positive.");
                        stack.Push(Math.Log10(val));
                        break;
                    case "ln":
                        if (val <= 0) throw new ArgumentException("Logarithm argument must be positive.");
                        stack.Push(Math.Log(val));
                        break;
                    case "exp": stack.Push(Math.Exp(val)); break;
                }
            }
        }

        if (stack.Count != 1) throw new ArgumentException("Invalid expression format.");
        return stack.Pop();
    }

    private static bool IsNumber(string token) =>
        double.TryParse(token, NumberStyles.Any, CultureInfo.InvariantCulture, out _);

    private static bool IsConstant(string token) => token is "pi" or "e";

    private static bool IsOperator(string token) => "+-*/%^".Contains(token) && token.Length == 1;

    private static bool IsFunction(string token) =>
        new[] { "sqrt", "sin", "cos", "tan", "abs", "floor", "ceil", "round", "log", "ln", "exp" }.Contains(
            token.ToLowerInvariant());

    private static int GetPrecedence(string op) => op switch
    {
        "^" => 4,
        "*" or "/" or "%" => 3,
        "+" or "-" => 2,
        _ => 0
    };

    private static string GetAssociation(string op) => op == "^" ? "Right" : "Left";
}