using System.Numerics;

namespace Tau.Inference.Routing;

/// <summary>Python's <c>round(x, ndigits)</c> on floats: round half to even on the exact binary value.</summary>
/// <remarks>
/// <c>Math.Round(x, 4, MidpointRounding.ToEven)</c> is not the same thing: it scales by 10^4 in floating
/// point first, and that multiplication can land a value that sits just off a midpoint exactly on it (or
/// the other way round). Python rounds the exact value the double holds, so this does too, with integer
/// arithmetic, then divides the rounded integer by 10^n, which IEEE division rounds correctly to the
/// nearest double -- the same double Python's <c>strtod</c> produces from the rounded decimal.
/// </remarks>
internal static class PyRound
{
    /// <summary>Round a finite <paramref name="x"/> to <paramref name="ndigits"/> decimal places as Python does.</summary>
    internal static double Round(double x, int ndigits)
    {
        if (!double.IsFinite(x) || x == 0.0)
        {
            return x;
        }

        ArgumentOutOfRangeException.ThrowIfNegative(ndigits);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ndigits, 15);

        var bits = BitConverter.DoubleToInt64Bits(x);
        var negative = bits < 0;
        var exponentBits = (int)((bits >> 52) & 0x7FF);
        var fraction = bits & 0xFFFFFFFFFFFFFL;
        long mantissa;
        int exponent;
        if (exponentBits == 0)
        {
            mantissa = fraction;
            exponent = -1074;
        }
        else
        {
            mantissa = fraction | (1L << 52);
            exponent = exponentBits - 1075;
        }

        if (exponent >= 0)
        {
            return x; // already an integer
        }

        // |x| * 10^n = mantissa * 10^n / 2^-exponent, rounded half to even.
        var numerator = new BigInteger(mantissa) * BigInteger.Pow(10, ndigits);
        var denominator = BigInteger.One << -exponent;
        var quotient = BigInteger.DivRem(numerator, denominator, out var remainder);
        var twice = remainder << 1;
        var cmp = twice.CompareTo(denominator);
        if (cmp > 0 || (cmp == 0 && !quotient.IsEven))
        {
            quotient += 1;
        }

        var result = (double)quotient / Math.Pow(10, ndigits);
        return negative ? -result : result;
    }
}
