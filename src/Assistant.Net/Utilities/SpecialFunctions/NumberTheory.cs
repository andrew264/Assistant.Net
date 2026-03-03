namespace Assistant.Net.Utilities;

public static partial class SpecialFunctions
{
    public static double Gcd(double a, double b)
    {
        if (!IsInteger(a) || !IsInteger(b))
            throw new ArgumentException("GCD arguments must be integers.");

        var x = (long)Math.Abs(a);
        var y = (long)Math.Abs(b);

        while (y != 0)
        {
            var temp = y;
            y = x % y;
            x = temp;
        }

        return x;
    }

    public static double Lcm(double a, double b)
    {
        if (!IsInteger(a) || !IsInteger(b))
            throw new ArgumentException("LCM arguments must be integers.");

        if (Math.Abs(a) < 1e-10 || Math.Abs(b) < 1e-10) return 0;

        var x = (long)Math.Abs(a);
        var y = (long)Math.Abs(b);

        var aa = x;
        var bb = y;
        while (bb != 0)
        {
            var temp = bb;
            bb = aa % bb;
            aa = temp;
        }

        var gcd = aa;
        return (double)x / gcd * y;
    }

    public static double Phi(double n)
    {
        if (!IsInteger(n))
            throw new ArgumentException("Phi argument must be an integer.");

        var val = (long)n;
        if (val <= 0)
            throw new ArgumentException("Phi argument must be positive.");

        var result = val;
        for (var i = 2L; i * i <= val; i++)
            if (val % i == 0)
            {
                while (val % i == 0)
                    val /= i;
                result -= result / i;
            }

        if (val > 1)
            result -= result / val;

        return result;
    }

    public static double IsPrime(double n)
    {
        if (!IsInteger(n)) return 0;

        var val = (long)n;
        switch (val)
        {
            case < 2:
                return 0;
            case 2 or 3:
                return 1;
        }

        if (val % 2 == 0 || val % 3 == 0) return 0;

        for (var i = 5L; i * i <= val; i += 6)
            if (val % i == 0 || val % (i + 2) == 0)
                return 0;

        return 1;
    }

    private static bool IsInteger(double d) => Math.Abs(d % 1) < 1e-10 && d is >= long.MinValue and <= long.MaxValue;
}