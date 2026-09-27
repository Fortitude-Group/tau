namespace Tau.Inference.Routing;

/// <summary>
/// Python 3.12 character predicates over code points, answered from <see cref="PyUnicodeTables"/>
/// rather than from .NET's own Unicode data, which is a different Unicode version.
/// </summary>
internal static partial class PyUnicodeTables
{
    /// <summary>Python's <c>str.isalpha()</c> for one code point.</summary>
    internal static bool IsAlpha(int cp) => InRanges(Alpha, cp);

    /// <summary>Python's <c>str.isnumeric()</c> for one code point.</summary>
    internal static bool IsNumeric(int cp) => InRanges(Numeric, cp);

    /// <summary>Python's <c>str.isdecimal()</c> for one code point (also what <c>re</c>'s <c>\d</c> matches).</summary>
    internal static bool IsDecimal(int cp) => InRanges(Decimal, cp);

    /// <summary>Python's <c>str.isupper()</c> for a one-code-point string.</summary>
    internal static bool IsUpper(int cp) => InRanges(Upper, cp);

    /// <summary><c>unicodedata.combining(ch) != 0</c>.</summary>
    internal static bool IsCombining(int cp) => InRanges(Combining, cp);

    /// <summary><c>re</c>'s <c>\w</c> with <c>re.UNICODE</c>: alphabetic, numeric or underscore.</summary>
    internal static bool IsWord(int cp) => cp == '_' || IsAlpha(cp) || IsNumeric(cp);

    /// <summary><c>re</c>'s <c>[^\W\d_]</c>: a word character that is neither a decimal digit nor underscore.</summary>
    internal static bool IsLetterClass(int cp) => cp != '_' && (IsAlpha(cp) || IsNumeric(cp)) && !IsDecimal(cp);

    /// <summary>
    /// Python's one-character <c>str.lower()</c> for every code point except U+0130, whose lower case is
    /// two code points and which callers handle themselves.
    /// </summary>
    internal static int Lower(int cp)
    {
        var i = Array.BinarySearch(LowerFrom, cp);
        return i >= 0 ? LowerTo[i] : cp;
    }

    private static bool InRanges(int[] flat, int cp)
    {
        // Find the last range start <= cp, then check its end.
        int lo = 0, hi = (flat.Length / 2) - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >>> 1;
            if (flat[2 * mid] <= cp)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return hi >= 0 && cp <= flat[(2 * hi) + 1];
    }
}
