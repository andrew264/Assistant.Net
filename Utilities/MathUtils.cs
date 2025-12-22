using System.Globalization;
using System.Text;

namespace Assistant.Net.Utilities;

public static class MathUtils
{
    private static readonly Dictionary<string, OperatorInfo> Operators = new()
    {
        { "+", new OperatorInfo(2, false) },
        { "-", new OperatorInfo(2, false) },
        { "*", new OperatorInfo(3, false) },
        { "/", new OperatorInfo(3, false) },
        { "%", new OperatorInfo(3, false) },
        { "^", new OperatorInfo(4, true) }
    };

    private static readonly Dictionary<string, double> Constants = new()
    {
        { "pi", Math.PI },
        { "e", Math.E }
    };

    private static readonly Dictionary<string, FunctionInfo> Functions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Unary
        { "sqrt", new FunctionInfo(1, args => Math.Sqrt(ValidatePos(args[0], "sqrt"))) },
        { "abs", new FunctionInfo(1, args => Math.Abs(args[0])) },
        { "floor", new FunctionInfo(1, args => Math.Floor(args[0])) },
        { "ceil", new FunctionInfo(1, args => Math.Ceiling(args[0])) },
        { "round", new FunctionInfo(1, args => Math.Round(args[0])) },
        { "exp", new FunctionInfo(1, args => Math.Exp(args[0])) },

        // Logarithms
        { "ln", new FunctionInfo(1, args => Math.Log(ValidatePos(args[0], "ln"))) }, // Natural log
        { "log", new FunctionInfo(1, args => Math.Log10(ValidatePos(args[0], "log"))) }, // Base 10
        { "log2", new FunctionInfo(1, args => Math.Log2(ValidatePos(args[0], "log2"))) }, // Base 2

        // Trig (Radians)
        { "sin", new FunctionInfo(1, args => Math.Sin(args[0])) },
        { "cos", new FunctionInfo(1, args => Math.Cos(args[0])) },
        { "tan", new FunctionInfo(1, args => Math.Tan(args[0])) },
        { "asin", new FunctionInfo(1, args => Math.Asin(args[0])) },
        { "acos", new FunctionInfo(1, args => Math.Acos(args[0])) },
        { "atan", new FunctionInfo(1, args => Math.Atan(args[0])) },

        // Trig (Degrees)
        { "sind", new FunctionInfo(1, args => Math.Sin(args[0] * (Math.PI / 180.0))) },
        { "cosd", new FunctionInfo(1, args => Math.Cos(args[0] * (Math.PI / 180.0))) },
        { "tand", new FunctionInfo(1, args => Math.Tan(args[0] * (Math.PI / 180.0))) },

        // Binary
        { "min", new FunctionInfo(2, args => Math.Min(args[0], args[1])) },
        { "max", new FunctionInfo(2, args => Math.Max(args[0], args[1])) },
        { "pow", new FunctionInfo(2, args => Math.Pow(args[0], args[1])) },
        { "atan2", new FunctionInfo(2, args => Math.Atan2(args[0], args[1])) }
    };

    private static double ValidatePos(double val, string funcName)
    {
        if (val < 0) throw new ArgumentException($"Argument for '{funcName}' must be non-negative.");
        return val;
    }

    public static double Evaluate(string expression)
    {
        var tokens = Tokenize(expression);
        var implicitTokens = AddImplicitMultiplication(tokens);
        var rpn = ShuntingYard(implicitTokens);
        return EvaluateRpn(rpn);
    }

    private static List<Token> Tokenize(string expression)
    {
        var tokens = new List<Token>();
        var sb = new StringBuilder();

        for (var i = 0; i < expression.Length; i++)
        {
            var c = expression[i];

            if (char.IsWhiteSpace(c)) continue;

            if (char.IsDigit(c) || c == '.')
            {
                sb.Clear();
                var dotCount = 0;

                if (c == '.') dotCount++;
                sb.Append(c);

                while (i + 1 < expression.Length)
                {
                    var next = expression[i + 1];
                    if (char.IsDigit(next))
                    {
                        sb.Append(next);
                        i++;
                    }
                    else if (next == '.')
                    {
                        dotCount++;
                        if (dotCount > 1)
                            throw new ArgumentException($"Invalid number format: multiple dots in '{sb}.'");
                        sb.Append(next);
                        i++;
                    }
                    else
                    {
                        break;
                    }
                }

                if (sb.ToString() == ".") throw new ArgumentException("Invalid token: standalone '.'");
                tokens.Add(new Token(sb.ToString(), TokenType.Number));
            }
            else if (char.IsLetter(c))
            {
                sb.Clear();
                sb.Append(c);
                while (i + 1 < expression.Length && char.IsLetter(expression[i + 1])) sb.Append(expression[++i]);

                var identifier = sb.ToString().ToLowerInvariant();
                if (Constants.ContainsKey(identifier))
                    tokens.Add(new Token(identifier, TokenType.Constant));
                else if (Functions.ContainsKey(identifier))
                    tokens.Add(new Token(identifier, TokenType.Function));
                else
                    throw new ArgumentException($"Unknown identifier: '{identifier}'");
            }
            else
            {
                var s = c.ToString();
                if (Operators.ContainsKey(s))
                {
                    if (s == "-" && (tokens.Count == 0 || IsUnarySource(tokens.Last())))
                    {
                        tokens.Add(new Token("0", TokenType.Number));
                        tokens.Add(new Token("-", TokenType.Operator));
                    }
                    else
                    {
                        tokens.Add(new Token(s, TokenType.Operator));
                    }
                }
                else if (s is "(" or ")" or ",")
                {
                    tokens.Add(new Token(s, TokenType.Separator));
                }
                else
                {
                    throw new ArgumentException($"Invalid character encountered: {c}");
                }
            }
        }

        return tokens;
    }

    private static bool IsUnarySource(Token t) =>
        t.Type == TokenType.Operator ||
        t is { Type: TokenType.Separator, Value: "(" or "," };

    private static List<Token> AddImplicitMultiplication(List<Token> tokens)
    {
        var result = new List<Token>();

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];

            if (i > 0)
            {
                var prev = tokens[i - 1];

                var isPrevOperand = prev.Type == TokenType.Number || prev.Type == TokenType.Constant ||
                                    prev is { Type: TokenType.Separator, Value: ")" };
                var isCurrOperand = token.Type == TokenType.Number || token.Type == TokenType.Constant ||
                                    token.Type == TokenType.Function ||
                                    token is { Type: TokenType.Separator, Value: "(" };

                if (isPrevOperand && isCurrOperand) result.Add(new Token("*", TokenType.Operator));
            }

            result.Add(token);
        }

        return result;
    }

    private static Queue<Token> ShuntingYard(List<Token> tokens)
    {
        var outputQueue = new Queue<Token>();
        var operatorStack = new Stack<Token>();

        foreach (var token in tokens)
            switch (token.Type)
            {
                case TokenType.Number:
                case TokenType.Constant:
                    outputQueue.Enqueue(token);
                    break;

                case TokenType.Function:
                    operatorStack.Push(token);
                    break;

                case TokenType.Separator:
                    if (token.Value == ",")
                    {
                        var foundParen = false;
                        while (operatorStack.Count > 0)
                        {
                            if (operatorStack.Peek().Value == "(")
                            {
                                foundParen = true;
                                break;
                            }

                            outputQueue.Enqueue(operatorStack.Pop());
                        }

                        if (!foundParen) throw new ArgumentException("Misplaced comma or mismatched parentheses.");
                    }
                    else if (token.Value == "(")
                    {
                        operatorStack.Push(token);
                    }
                    else if (token.Value == ")")
                    {
                        var foundOpen = false;
                        while (operatorStack.Count > 0)
                        {
                            if (operatorStack.Peek().Value == "(")
                            {
                                foundOpen = true;
                                break;
                            }

                            outputQueue.Enqueue(operatorStack.Pop());
                        }

                        if (!foundOpen)
                            throw new ArgumentException("Mismatched parentheses: too many closing parentheses.");

                        operatorStack.Pop();

                        if (operatorStack.Count > 0 && operatorStack.Peek().Type == TokenType.Function)
                            outputQueue.Enqueue(operatorStack.Pop());
                    }

                    break;

                case TokenType.Operator:
                    while (operatorStack.Count > 0 && operatorStack.Peek().Type == TokenType.Operator)
                    {
                        var o1 = token.Value;
                        var o2Token = operatorStack.Peek();
                        var o2 = o2Token.Value;

                        var op1Info = Operators[o1];
                        var op2Info = Operators[o2];

                        if ((!op1Info.RightAssociative && op1Info.Precedence <= op2Info.Precedence) ||
                            (op1Info.RightAssociative && op1Info.Precedence < op2Info.Precedence))
                            outputQueue.Enqueue(operatorStack.Pop());
                        else
                            break;
                    }

                    operatorStack.Push(token);
                    break;
            }

        while (operatorStack.Count > 0)
        {
            var op = operatorStack.Pop();
            if (op.Value is "(" or ")") throw new ArgumentException("Mismatched parentheses.");
            outputQueue.Enqueue(op);
        }

        return outputQueue;
    }

    private static double EvaluateRpn(Queue<Token> rpnQueue)
    {
        var stack = new Stack<double>();

        while (rpnQueue.Count > 0)
        {
            var token = rpnQueue.Dequeue();

            switch (token.Type)
            {
                case TokenType.Number when double.TryParse(token.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var val):
                    stack.Push(val);
                    break;
                case TokenType.Number:
                    throw new ArgumentException($"Failed to parse number: {token.Value}");
                case TokenType.Constant:
                    stack.Push(Constants[token.Value]);
                    break;
                case TokenType.Operator when stack.Count < 2:
                    throw new ArgumentException($"Missing operands for operator '{token.Value}'.");
                case TokenType.Operator:
                {
                    var right = stack.Pop();
                    var left = stack.Pop();
                    var result = 0.0;

                    switch (token.Value)
                    {
                        case "+": result = left + right; break;
                        case "-": result = left - right; break;
                        case "*": result = left * right; break;
                        case "/":
                            if (Math.Abs(right) < 1e-15) throw new DivideByZeroException();
                            result = left / right;
                            break;
                        case "%": result = left % right; break;
                        case "^": result = Math.Pow(left, right); break;
                    }

                    stack.Push(result);
                    break;
                }
                case TokenType.Function:
                {
                    var funcName = token.Value;
                    if (!Functions.TryGetValue(funcName, out var funcInfo))
                        throw new ArgumentException($"Unknown function '{funcName}'.");

                    if (stack.Count < funcInfo.Arity)
                        throw new ArgumentException($"Function '{funcName}' requires {funcInfo.Arity} arguments.");

                    var args = new double[funcInfo.Arity];
                    for (var i = funcInfo.Arity - 1; i >= 0; i--) args[i] = stack.Pop();

                    stack.Push(funcInfo.Evaluator(args));
                    break;
                }
            }
        }

        if (stack.Count != 1) throw new ArgumentException("Invalid expression format.");
        return stack.Pop();
    }

    private enum TokenType
    {
        Number,
        Operator,
        Function,
        Separator,
        Constant
    }

    private readonly struct Token
    {
        public string Value { get; }
        public TokenType Type { get; }

        public Token(string value, TokenType type)
        {
            Value = value;
            Type = type;
        }

        public override string ToString() => Value;
    }

    private record FunctionInfo(int Arity, Func<double[], double> Evaluator);

    private record OperatorInfo(int Precedence, bool RightAssociative);
}