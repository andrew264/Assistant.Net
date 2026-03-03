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
        { "pi", MathConstants.Pi },
        { "e", MathConstants.E },
        { "gold", MathConstants.GoldenRatio },
        { "tau", MathConstants.Pi2 }
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
        { "sind", new FunctionInfo(1, args => Math.Sin(args[0] * (MathConstants.Pi / 180.0))) },
        { "cosd", new FunctionInfo(1, args => Math.Cos(args[0] * (MathConstants.Pi / 180.0))) },
        { "tand", new FunctionInfo(1, args => Math.Tan(args[0] * (MathConstants.Pi / 180.0))) },

        // Binary
        { "min", new FunctionInfo(2, args => Math.Min(args[0], args[1])) },
        { "max", new FunctionInfo(2, args => Math.Max(args[0], args[1])) },
        { "pow", new FunctionInfo(2, args => Math.Pow(args[0], args[1])) },
        { "atan2", new FunctionInfo(2, args => Math.Atan2(args[0], args[1])) },
        { "root", new FunctionInfo(2, args => Math.Pow(args[0], 1.0 / args[1])) },
        { "hypot", new FunctionInfo(2, args => Math.Sqrt(args[0] * args[0] + args[1] * args[1])) },

        // Random
        { "rand", new FunctionInfo(0, _ => Random.Shared.NextDouble()) },
        {
            "randint",
            new FunctionInfo(2,
                args => Random.Shared.Next((int)Math.Min(args[0], args[1]), (int)Math.Max(args[0], args[1]) + 1))
        },

        // Number Theory
        { "gcd", new FunctionInfo(2, args => SpecialFunctions.Gcd(args[0], args[1])) },
        { "lcm", new FunctionInfo(2, args => SpecialFunctions.Lcm(args[0], args[1])) },
        { "phi", new FunctionInfo(1, args => SpecialFunctions.Phi(args[0])) },
        { "isprime", new FunctionInfo(1, args => SpecialFunctions.IsPrime(args[0])) },

        // Combinatorics
        {
            "ncr", new FunctionInfo(2, args =>
            {
                var n = args[0];
                var k = args[1];

                if (!IsNonNegativeInteger(n) || !IsNonNegativeInteger(k))
                    return Math.Exp(
                        SpecialFunctions.GammaLn(n + 1)
                        - SpecialFunctions.GammaLn(k + 1)
                        - SpecialFunctions.GammaLn(n - k + 1));

                if (k < 0 || k > n) return 0;
                if (k == 0 || Math.Abs(k - n) < 1e-10) return 1;
                if (k > n / 2) k = n - k;
                double result = 1;
                for (var i = 1; i <= k; i++) result = result * (n - i + 1) / i;
                return Math.Round(result);
            })
        },
        {
            "npr", new FunctionInfo(2, args =>
            {
                var n = args[0];
                var k = args[1];

                if (!IsNonNegativeInteger(n) || !IsNonNegativeInteger(k))
                    return Math.Exp(
                        SpecialFunctions.GammaLn(n + 1)
                        - SpecialFunctions.GammaLn(n - k + 1));

                if (k < 0 || k > n) return 0;
                double result = 1;
                for (var i = 0; i < k; i++) result *= n - i;
                return result;
            })
        },
        {
            "catalan", new FunctionInfo(1, args =>
            {
                var n = args[0];
                var result = Math.Exp(
                    SpecialFunctions.GammaLn(2 * n + 1)
                    - SpecialFunctions.GammaLn(n + 2)
                    - SpecialFunctions.GammaLn(n + 1));
                return IsNonNegativeInteger(n) ? Math.Round(result) : result;
            })
        },
        {
            "stirling", new FunctionInfo(2, args =>
            {
                var n = args[0];
                var k = args[1];

                if (!IsNonNegativeInteger(n) || !IsNonNegativeInteger(k))
                    throw new ArgumentException("Stirling numbers require non-negative integer arguments.");

                if (k == 0) return n == 0 ? 1 : 0;
                if (k > n) return 0;
                if (Math.Abs(k - n) < 1e-10) return 1;

                double sum = 0;
                for (var j = 1; j <= k; j++)
                {
                    var sign = (k - j) % 2 == 0 ? 1.0 : -1.0;

                    var logBinom = SpecialFunctions.GammaLn(k + 1)
                                   - SpecialFunctions.GammaLn(j + 1)
                                   - SpecialFunctions.GammaLn(k - j + 1);

                    var term = Math.Exp(logBinom) * Math.Pow(j, n);
                    sum += sign * term;
                }

                return Math.Round(sum / SpecialFunctions.Gamma(k + 1));
            })
        }
    };

    private static double ValidatePos(double val, string funcName) => val < 0
        ? throw new ArgumentException($"Argument for '{funcName}' must be non-negative.")
        : val;

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
                var hasExponent = false;

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
                        if (hasExponent) break;

                        dotCount++;
                        if (dotCount > 1)
                            throw new ArgumentException($"Invalid number format: multiple dots in '{sb}.'");
                        sb.Append(next);
                        i++;
                    }
                    else if (!hasExponent && next is 'e' or 'E')
                    {
                        var isScientific = false;
                        if (i + 2 < expression.Length)
                        {
                            var afterNext = expression[i + 2];
                            if (char.IsDigit(afterNext) || afterNext == '+' || afterNext == '-') isScientific = true;
                        }

                        if (isScientific)
                        {
                            hasExponent = true;
                            sb.Append(next);
                            i++;

                            var signChar = expression[i + 1];
                            if (signChar is '+' or '-')
                            {
                                sb.Append(signChar);
                                i++;
                            }
                        }
                        else
                        {
                            break;
                        }
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
            else if (c == '!')
            {
                tokens.Add(new Token("!", TokenType.PostfixOperator));
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

                var isPrevOperand = prev.Type is TokenType.Number or TokenType.Constant or TokenType.PostfixOperator
                                    || prev is { Type: TokenType.Separator, Value: ")" };
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
                case TokenType.PostfixOperator:
                    outputQueue.Enqueue(token);
                    break;
                case TokenType.Separator:
                    switch (token.Value)
                    {
                        case ",":
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
                            break;
                        }
                        case "(":
                            operatorStack.Push(token);
                            break;
                        case ")":
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
                            break;
                        }
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
                case TokenType.Number when double.TryParse(token.Value, NumberStyles.Any, CultureInfo.InvariantCulture,
                    out var val):
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
                case TokenType.PostfixOperator when token.Value == "!":
                {
                    if (stack.Count < 1) throw new ArgumentException("Missing operand for '!'.");
                    var operand = stack.Pop();
                    if (operand < 0)
                        throw new ArgumentException("Factorial undefined for negative numbers.");
                    stack.Push(SpecialFunctions.Gamma(operand + 1));
                    break;
                }
            }
        }

        if (stack.Count != 1) throw new ArgumentException("Invalid expression format.");
        return stack.Pop();
    }

    private static bool IsNonNegativeInteger(double value) =>
        value is >= 0 and < int.MaxValue && Math.Abs(value - Math.Round(value)) < 1e-10;

    private enum TokenType
    {
        Number,
        Operator,
        Function,
        Separator,
        Constant,
        PostfixOperator
    }

    private readonly struct Token(string value, TokenType type)
    {
        public string Value { get; } = value;
        public TokenType Type { get; } = type;
        public override string ToString() => Value;
    }

    private record FunctionInfo(int Arity, Func<double[], double> Evaluator);

    private record OperatorInfo(int Precedence, bool RightAssociative);
}