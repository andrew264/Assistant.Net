namespace Assistant.Net.Utilities;

public static partial class SpecialFunctions
{
    private const int GammaN = 10;
    private const double GammaR = 10.900511;

    private static readonly double[] GammaDk =
    [
        2.48574089138753565546e-5,
        1.05142378581721974210,
        -3.45687097222016235469,
        4.51227709466894823700,
        -2.98285225323576655721,
        1.05639711577126713077,
        -1.95428773191645869583e-1,
        1.70970543404441224307e-2,
        -5.71926117404305781283e-4,
        4.63399473359905636708e-6,
        -2.71994908488607703910e-9
    ];

    public static double Gamma(double z)
    {
        if (z < 0.5)
        {
            var s = GammaDk[0];
            for (var i = 1; i <= GammaN; i++) s += GammaDk[i] / (i - z);

            return Math.PI / (Math.Sin(Math.PI * z)
                              * s
                              * MathConstants.TwoSqrtEOverPi
                              * Math.Pow((0.5 - z + GammaR) / Math.E, 0.5 - z));
        }
        else
        {
            var s = GammaDk[0];
            for (var i = 1; i <= GammaN; i++) s += GammaDk[i] / (z + i - 1.0);

            return s * MathConstants.TwoSqrtEOverPi * Math.Pow((z - 0.5 + GammaR) / Math.E, z - 0.5);
        }
    }

    public static double GammaLn(double z)
    {
        if (z < 0.5)
        {
            var s = GammaDk[0];
            for (var i = 1; i <= GammaN; i++) s += GammaDk[i] / (i - z);

            return MathConstants.LnPi
                   - Math.Log(Math.Sin(Math.PI * z))
                   - Math.Log(s)
                   - MathConstants.LogTwoSqrtEOverPi
                   - (0.5 - z) * Math.Log((0.5 - z + GammaR) / Math.E);
        }
        else
        {
            var s = GammaDk[0];
            for (var i = 1; i <= GammaN; i++) s += GammaDk[i] / (z + i - 1.0);

            return Math.Log(s)
                   + MathConstants.LogTwoSqrtEOverPi
                   + (z - 0.5) * Math.Log((z - 0.5 + GammaR) / Math.E);
        }
    }
}