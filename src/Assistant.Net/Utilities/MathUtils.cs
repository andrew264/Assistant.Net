using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Assistant.Net.Utilities;

public static class MathUtils
{
    // Precedence: | (1) < & (2) < Shifts (3) < + (4) < * (5) < ^ (6)
    private static readonly Dictionary<string, OperatorInfo> Operators = new(StringComparer.Ordinal)
    {
        { "|", new OperatorInfo(1, false) }, { "&", new OperatorInfo(2, false) },
        { "<<", new OperatorInfo(3, false) }, { ">>", new OperatorInfo(3, false) },
        { "+", new OperatorInfo(4, false) }, { "-", new OperatorInfo(4, false) },
        { "*", new OperatorInfo(5, false) }, { "/", new OperatorInfo(5, false) },
        { "%", new OperatorInfo(5, false) }, { "^", new OperatorInfo(6, true) }
    };

    private static readonly Dictionary<string, double> Constants = new(StringComparer.OrdinalIgnoreCase)
    {
        { "pi", MathConstants.Pi }, { "e", MathConstants.E },
        { "gold", MathConstants.GoldenRatio }, { "tau", MathConstants.Pi2 }
    };

    private static readonly Dictionary<string, FunctionInfo> Functions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Unary
        {
            "sqrt", new FunctionInfo(1, static args => Math.Sqrt(args[0] < 0 ? throw new ArgumentException() : args[0]))
        },
        { "abs", new FunctionInfo(1, static args => Math.Abs(args[0])) },
        { "floor", new FunctionInfo(1, static args => Math.Floor(args[0])) },
        { "ceil", new FunctionInfo(1, static args => Math.Ceiling(args[0])) },
        { "round", new FunctionInfo(1, static args => Math.Round(args[0])) },
        { "exp", new FunctionInfo(1, static args => Math.Exp(args[0])) },

        // Logarithms
        { "ln", new FunctionInfo(1, static args => Math.Log(args[0] < 0 ? throw new ArgumentException() : args[0])) },
        {
            "log", new FunctionInfo(1, static args => Math.Log10(args[0] < 0 ? throw new ArgumentException() : args[0]))
        },
        {
            "log2", new FunctionInfo(1, static args => Math.Log2(args[0] < 0 ? throw new ArgumentException() : args[0]))
        },

        // Trig (Radians)
        { "sin", new FunctionInfo(1, static args => Math.Sin(args[0])) },
        { "cos", new FunctionInfo(1, static args => Math.Cos(args[0])) },
        { "tan", new FunctionInfo(1, static args => Math.Tan(args[0])) },
        { "asin", new FunctionInfo(1, static args => Math.Asin(args[0])) },
        { "acos", new FunctionInfo(1, static args => Math.Acos(args[0])) },
        { "atan", new FunctionInfo(1, static args => Math.Atan(args[0])) },

        // Trig (Degrees)
        { "sind", new FunctionInfo(1, static args => Math.Sin(args[0] * (MathConstants.Pi / 180.0))) },
        { "cosd", new FunctionInfo(1, static args => Math.Cos(args[0] * (MathConstants.Pi / 180.0))) },
        { "tand", new FunctionInfo(1, static args => Math.Tan(args[0] * (MathConstants.Pi / 180.0))) },

        // Binary
        { "min", new FunctionInfo(2, static args => Math.Min(args[0], args[1])) },
        { "max", new FunctionInfo(2, static args => Math.Max(args[0], args[1])) },
        { "pow", new FunctionInfo(2, static args => Math.Pow(args[0], args[1])) },
        { "atan2", new FunctionInfo(2, static args => Math.Atan2(args[0], args[1])) },
        { "root", new FunctionInfo(2, static args => Math.Pow(args[0], 1.0 / args[1])) },
        { "hypot", new FunctionInfo(2, static args => Math.Sqrt(args[0] * args[0] + args[1] * args[1])) },

        // Bitwise
        { "xor", new FunctionInfo(2, static args => (long)args[0] ^ (long)args[1]) },
        { "not", new FunctionInfo(1, static args => ~(long)args[0]) },

        // Random
        { "rand", new FunctionInfo(0, static _ => Random.Shared.NextDouble()) },
        {
            "randint",
            new FunctionInfo(2,
                static args => Random.Shared.Next((int)Math.Min(args[0], args[1]), (int)Math.Max(args[0], args[1]) + 1))
        },

        // Number Theory
        { "gcd", new FunctionInfo(2, static args => SpecialFunctions.Gcd(args[0], args[1])) },
        { "lcm", new FunctionInfo(2, static args => SpecialFunctions.Lcm(args[0], args[1])) },
        { "phi", new FunctionInfo(1, static args => SpecialFunctions.Phi(args[0])) },
        { "isprime", new FunctionInfo(1, static args => SpecialFunctions.IsPrime(args[0])) },

        // Combinatorics
        {
            "ncr", new FunctionInfo(2, static args =>
            {
                double n = args[0], k = args[1];
                if (!IsNonNegativeInteger(n) || !IsNonNegativeInteger(k))
                    return Math.Exp(SpecialFunctions.GammaLn(n + 1) - SpecialFunctions.GammaLn(k + 1) -
                                    SpecialFunctions.GammaLn(n - k + 1));
                if (k < 0 || k > n) return 0;
                if (k == 0 || Math.Abs(k - n) < 1e-10) return 1;
                if (k > n / 2) k = n - k;
                double res = 1;
                for (var i = 1; i <= k; i++) res = res * (n - i + 1) / i;
                return Math.Round(res);
            })
        },
        {
            "npr", new FunctionInfo(2, static args =>
            {
                double n = args[0], k = args[1];
                if (!IsNonNegativeInteger(n) || !IsNonNegativeInteger(k))
                    return Math.Exp(SpecialFunctions.GammaLn(n + 1) - SpecialFunctions.GammaLn(n - k + 1));
                if (k < 0 || k > n) return 0;
                double res = 1;
                for (var i = 0; i < k; i++) res *= n - i;
                return res;
            })
        },
        {
            "catalan", new FunctionInfo(1, static args =>
            {
                var n = args[0];
                var res = Math.Exp(SpecialFunctions.GammaLn(2 * n + 1) - SpecialFunctions.GammaLn(n + 2) -
                                   SpecialFunctions.GammaLn(n + 1));
                return IsNonNegativeInteger(n) ? Math.Round(res) : res;
            })
        },
        {
            "stirling", new FunctionInfo(2, static args =>
            {
                double n = args[0], k = args[1];
                if (!IsNonNegativeInteger(n) || !IsNonNegativeInteger(k)) throw new ArgumentException();
                if (k == 0) return n == 0 ? 1 : 0;
                if (k > n) return 0;
                if (Math.Abs(k - n) < 1e-10) return 1;
                double sum = 0;
                for (var j = 1; j <= k; j++)
                {
                    var sign = (k - j) % 2 == 0 ? 1.0 : -1.0;
                    sum += sign *
                           Math.Exp(SpecialFunctions.GammaLn(k + 1) - SpecialFunctions.GammaLn(j + 1) -
                                    SpecialFunctions.GammaLn(k - j + 1)) * Math.Pow(j, n);
                }

                return Math.Round(sum / SpecialFunctions.Gamma(k + 1));
            })
        }
    };

    private static readonly Dictionary<string, OperatorInfo>.AlternateLookup<ReadOnlySpan<char>> OpLookup =
        Operators.GetAlternateLookup<ReadOnlySpan<char>>();

    private static readonly Dictionary<string, double>.AlternateLookup<ReadOnlySpan<char>> ConstLookup =
        Constants.GetAlternateLookup<ReadOnlySpan<char>>();

    private static readonly Dictionary<string, FunctionInfo>.AlternateLookup<ReadOnlySpan<char>> FuncLookup =
        Functions.GetAlternateLookup<ReadOnlySpan<char>>();

    public static double Evaluate(string expression)
    {
        var max = expression.Length * 2;
        Token[]? r1 = null;
        Token[]? r2 = null;
        var tokens = max <= 512 ? stackalloc Token[max] : r1 = ArrayPool<Token>.Shared.Rent(max);
        var rpn = max <= 512 ? stackalloc Token[max] : r2 = ArrayPool<Token>.Shared.Rent(max);

        try
        {
            var tCount = Tokenize(expression, tokens);
            var rCount = ShuntingYard(tokens[..tCount], expression, rpn);
            return EvaluateRpn(rpn[..rCount], expression);
        }
        finally
        {
            if (r1 != null) ArrayPool<Token>.Shared.Return(r1);
            if (r2 != null) ArrayPool<Token>.Shared.Return(r2);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ReadOnlySpan<char> GetSpan(Token t, string e) =>
        t.Start switch { -1 => "0", -2 => "*", _ => e.AsSpan(t.Start, t.Length) };

    private static int Tokenize(string e, Span<Token> tokens)
    {
        var span = e.AsSpan();
        var count = 0;
        for (var i = 0; i < span.Length; i++)
        {
            var c = span[i];
            if (char.IsWhiteSpace(c)) continue;
            if (char.IsDigit(c) || c == '.')
            {
                var start = i;
                var dot = c == '.' ? 1 : 0;
                var exp = false;
                while (i + 1 < span.Length)
                {
                    var n = span[i + 1];
                    if (char.IsDigit(n))
                    {
                        i++;
                    }
                    else if (n == '.' && !exp)
                    {
                        dot++;
                        if (dot > 1) throw new ArgumentException();
                        i++;
                    }
                    else if (!exp && n is 'e' or 'E' && i + 2 < span.Length && (char.IsDigit(span[i + 2]) ||
                                 (span[i + 2] is '+' or '-' && i + 3 < span.Length && char.IsDigit(span[i + 3]))))
                    {
                        exp = true;
                        i++;
                        if (span[i + 1] is '+' or '-') i++;
                    }
                    else
                    {
                        break;
                    }
                }

                tokens[count++] = new Token(start, i - start + 1, TokenType.Number);
            }
            else if (char.IsLetter(c))
            {
                var start = i;
                while (i + 1 < span.Length && char.IsLetter(span[i + 1])) i++;
                var len = i - start + 1;
                var id = span.Slice(start, len);
                if (ConstLookup.ContainsKey(id)) tokens[count++] = new Token(start, len, TokenType.Constant);
                else if (FuncLookup.ContainsKey(id)) tokens[count++] = new Token(start, len, TokenType.Function);
                else throw new ArgumentException();
            }
            else if (c == '!')
            {
                tokens[count++] = new Token(i, 1, TokenType.PostfixOperator);
            }
            else if (i + 1 < span.Length && ((c == '<' && span[i + 1] == '<') || (c == '>' && span[i + 1] == '>')))
            {
                tokens[count++] = new Token(i, 2, TokenType.Operator);
                i++;
            }
            else if (c is '|' or '&' or '+' or '-' or '*' or '/' or '%' or '^')
            {
                if (c == '-' && (count == 0 || IsUnarySource(tokens[count - 1], e)))
                    tokens[count++] = new Token(-1, 0, TokenType.Number);
                tokens[count++] = new Token(i, 1, TokenType.Operator);
            }
            else if (c is '(' or ')' or ',')
            {
                tokens[count++] = new Token(i, 1, TokenType.Separator);
            }
        }

        return count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsUnarySource(Token t, string e)
    {
        if (t.Type == TokenType.Operator) return true;
        if (t.Type != TokenType.Separator) return false;
        var s = GetSpan(t, e);
        return s.Length == 1 && (s[0] == '(' || s[0] == ',');
    }

    private static int ShuntingYard(ReadOnlySpan<Token> tokens, string e, Span<Token> output)
    {
        var max = tokens.Length;
        Token[]? r = null;
        var opStack = max <= 256 ? stackalloc Token[max] : r = ArrayPool<Token>.Shared.Rent(max);
        int opIdx = 0, outIdx = 0;
        const int pStar = 5;

        try
        {
            for (var i = 0; i < tokens.Length; i++)
            {
                var t = tokens[i];
                if (i > 0)
                {
                    var p = tokens[i - 1];
                    var iPrev = p.Type is TokenType.Number or TokenType.Constant or TokenType.PostfixOperator ||
                                (p.Type == TokenType.Separator && GetSpan(p, e)[0] == ')');
                    var iCurr = t.Type is TokenType.Number or TokenType.Constant or TokenType.Function ||
                                (t.Type == TokenType.Separator && GetSpan(t, e)[0] == '(');
                    if (iPrev && iCurr)
                    {
                        while (opIdx > 0 && opStack[opIdx - 1].Type == TokenType.Operator &&
                               OpLookup[GetSpan(opStack[opIdx - 1], e)].Precedence >= pStar)
                            output[outIdx++] = opStack[--opIdx];
                        opStack[opIdx++] = new Token(-2, 0, TokenType.Operator);
                    }
                }

                switch (t.Type)
                {
                    case TokenType.Number:
                    case TokenType.Constant:
                    case TokenType.PostfixOperator: output[outIdx++] = t; break;
                    case TokenType.Function: opStack[opIdx++] = t; break;
                    case TokenType.Separator:
                        var c = GetSpan(t, e)[0];
                        if (c == '(')
                        {
                            opStack[opIdx++] = t;
                        }
                        else if (c == ',')
                        {
                            while (opIdx > 0 && GetSpan(opStack[opIdx - 1], e)[0] != '(')
                                output[outIdx++] = opStack[--opIdx];
                        }
                        else
                        {
                            while (opIdx > 0 && GetSpan(opStack[opIdx - 1], e)[0] != '(')
                                output[outIdx++] = opStack[--opIdx];
                            if (opIdx-- > 0 && opIdx > 0 && opStack[opIdx - 1].Type == TokenType.Function)
                                output[outIdx++] = opStack[--opIdx];
                        }

                        break;
                    case TokenType.Operator:
                        var i1 = OpLookup[GetSpan(t, e)];
                        while (opIdx > 0 && opStack[opIdx - 1].Type == TokenType.Operator)
                        {
                            var i2 = OpLookup[GetSpan(opStack[opIdx - 1], e)];
                            if ((!i1.RightAssociative && i1.Precedence <= i2.Precedence) ||
                                (i1.RightAssociative && i1.Precedence < i2.Precedence))
                                output[outIdx++] = opStack[--opIdx];
                            else break;
                        }

                        opStack[opIdx++] = t;
                        break;
                }
            }

            while (opIdx > 0) output[outIdx++] = opStack[--opIdx];
            return outIdx;
        }
        finally
        {
            if (r != null) ArrayPool<Token>.Shared.Return(r);
        }
    }

    private static double EvaluateRpn(ReadOnlySpan<Token> rpn, string e)
    {
        var max = rpn.Length;
        double[]? r = null;
        var stack = max <= 256 ? stackalloc double[max] : r = ArrayPool<double>.Shared.Rent(max);
        var idx = 0;
        try
        {
            for (var i = 0; i < rpn.Length; i++)
            {
                var t = rpn[i];
                var s = GetSpan(t, e);
                switch (t.Type)
                {
                    case TokenType.Number: stack[idx++] = double.Parse(s, CultureInfo.InvariantCulture); break;
                    case TokenType.Constant: stack[idx++] = ConstLookup[s]; break;
                    case TokenType.Operator:
                        var rv = stack[--idx];
                        var lv = stack[--idx];
                        stack[idx++] = s.Length == 1
                            ? s[0] switch
                            {
                                '+' => lv + rv, '-' => lv - rv, '*' => lv * rv, '/' => lv / rv, '%' => lv % rv,
                                '^' => Math.Pow(lv, rv), '&' => (long)lv & (long)rv, '|' => (long)lv | (long)rv,
                                _ => 0
                            }
                            : s[0] == '<'
                                ? (long)lv << (int)rv
                                : (long)lv >> (int)rv;
                        break;
                    case TokenType.Function:
                        var fi = FuncLookup[s];
                        idx -= fi.Arity;
                        stack[idx] = fi.Evaluator(stack.Slice(idx, fi.Arity));
                        idx++;
                        break;
                    case TokenType.PostfixOperator: stack[idx - 1] = SpecialFunctions.Gamma(stack[idx - 1] + 1); break;
                }
            }

            return stack[0];
        }
        finally
        {
            if (r != null) ArrayPool<double>.Shared.Return(r);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsNonNegativeInteger(double v) =>
        v is >= 0 and < int.MaxValue && Math.Abs(v - Math.Round(v)) < 1e-10;

    private delegate double FunctionEvaluator(ReadOnlySpan<double> args);

    private enum TokenType
    {
        Number,
        Operator,
        Function,
        Separator,
        Constant,
        PostfixOperator
    }

    private readonly struct Token(int start, int length, TokenType type)
    {
        public readonly int Start = start, Length = length;
        public readonly TokenType Type = type;
    }

    private readonly struct FunctionInfo(int arity, FunctionEvaluator evaluator)
    {
        public readonly int Arity = arity;
        public readonly FunctionEvaluator Evaluator = evaluator;
    }

    private readonly struct OperatorInfo(int precedence, bool rightAssociative)
    {
        public readonly int Precedence = precedence;
        public readonly bool RightAssociative = rightAssociative;
    }
}