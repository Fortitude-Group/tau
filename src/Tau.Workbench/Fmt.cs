using System.Globalization;

namespace Tau.Workbench;

/// <summary>
/// Culture-invariant number formatting for notes and the report, so output never depends on the
/// machine's locale (constitution IV).
/// </summary>
public static class Fmt
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>A fraction as a percentage, for example 0.1234 → "12.3%"; "n/a" for null.</summary>
    /// <param name="fraction">The fraction.</param>
    /// <param name="decimals">Decimal places.</param>
    public static string Pct(double? fraction, int decimals = 1) =>
        fraction is { } f && double.IsFinite(f) ? (f * 100).ToString("F" + decimals, Inv) + "%" : "n/a";

    /// <summary>A number with a fixed format, for example "0.000"; "n/a" for null.</summary>
    /// <param name="value">The value.</param>
    /// <param name="format">A .NET numeric format.</param>
    public static string Num(double? value, string format = "0.000") =>
        value is { } v && double.IsFinite(v) ? v.ToString(format, Inv) : "n/a";

    /// <summary>A whole number with thousands separators, for example "1,000".</summary>
    /// <param name="value">The value.</param>
    public static string Int(long value) => value.ToString("N0", Inv);

    /// <summary>Pounds with a sensible number of decimals for small and large amounts; "n/a" for null.</summary>
    /// <param name="gbp">The amount in pounds.</param>
    public static string Gbp(double? gbp) => gbp is not { } g || !double.IsFinite(g) ? "n/a"
        : Math.Abs(g) >= 100 ? "£" + g.ToString("N0", Inv)
        : Math.Abs(g) >= 1 ? "£" + g.ToString("N2", Inv)
        : "£" + g.ToString("0.0000", Inv);

    /// <summary>US dollars, formatted as <see cref="Gbp"/>.</summary>
    /// <param name="usd">The amount in dollars.</param>
    public static string Usd(double? usd) => Gbp(usd).Replace("£", "$", StringComparison.Ordinal);
}
