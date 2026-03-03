namespace Assistant.Net.Utilities;

public static class SpecialFunctions
{
    private const double TwoSqrtEOverPi = 1.8603827342052657173362492472666631120594218414085755;
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
                              * TwoSqrtEOverPi
                              * Math.Pow((0.5 - z + GammaR) / Math.E, 0.5 - z));
        }
        else
        {
            var s = GammaDk[0];
            for (var i = 1; i <= GammaN; i++) s += GammaDk[i] / (z + i - 1.0);

            return s * TwoSqrtEOverPi * Math.Pow((z - 0.5 + GammaR) / Math.E, z - 0.5);
        }
    }
}