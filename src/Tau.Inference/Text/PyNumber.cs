using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tau.Inference.Text;

/// <summary>
/// Formats a JSON number the way CPython prints the value <c>json.loads</c> produced for it.
/// </summary>
/// <remarks>
/// <para>
/// <c>json.loads</c> turns a number containing <c>.</c>, <c>e</c> or <c>E</c> into a <c>float</c> and anything
/// else into an arbitrary-precision <c>int</c>, so the decision needs the number's original spelling, which
/// a <see cref="JsonValue"/> parsed from text keeps in its backing <see cref="JsonElement"/>.
/// </para>
/// <para>
/// Floats print as <c>repr(float)</c>: the shortest digit string that round-trips, in fixed notation when the
/// decimal exponent is in [-4, 16) and scientific notation (<c>1e-05</c>, <c>1.5e+16</c>) otherwise, with
/// <c>.0</c> added to integral fixed-notation values. .NET's round-trip formatting produces the same shortest
/// digits; only the layout is Python's.
/// </para>
/// </remarks>
internal static class PyNumber
{
    /// <summary>Formats a number as <c>json.dumps</c> (<paramref name="json"/>) or <c>repr</c> would print it.</summary>
    /// <param name="value">A JSON value whose kind is <see cref="JsonValueKind.Number"/>.</param>
    /// <param name="json">True for <c>json.dumps</c> spelling of infinities (<c>Infinity</c>), false for repr (<c>inf</c>).</param>
    internal static string Format(JsonValue value, bool json)
    {
        if (value.TryGetValue<JsonElement>(out var element))
        {
            return FormatRaw(element.GetRawText(), json);
        }

        // A value built in code rather than parsed: classify by its CLR type, as Python would by its type.
        if (value.TryGetValue<double>(out var d)) return FormatFloat(d, json);
        if (value.TryGetValue<float>(out var f)) return FormatFloat(f, json);
        if (value.TryGetValue<Half>(out var h)) return FormatFloat((double)h, json);
        if (value.TryGetValue<decimal>(out var m)) return FormatFloat((double)m, json);
        if (value.TryGetValue<long>(out var l)) return l.ToString(CultureInfo.InvariantCulture);
        if (value.TryGetValue<int>(out var i)) return i.ToString(CultureInfo.InvariantCulture);
        if (value.TryGetValue<ulong>(out var ul)) return ul.ToString(CultureInfo.InvariantCulture);
        if (value.TryGetValue<uint>(out var ui)) return ui.ToString(CultureInfo.InvariantCulture);
        if (value.TryGetValue<short>(out var s)) return s.ToString(CultureInfo.InvariantCulture);
        if (value.TryGetValue<ushort>(out var us)) return us.ToString(CultureInfo.InvariantCulture);
        if (value.TryGetValue<byte>(out var b)) return b.ToString(CultureInfo.InvariantCulture);
        if (value.TryGetValue<sbyte>(out var sb)) return sb.ToString(CultureInfo.InvariantCulture);
        return FormatRaw(value.ToJsonString(), json);
    }

    /// <summary>Formats a number from its JSON spelling.</summary>
    internal static string FormatRaw(string raw, bool json)
    {
        if (raw.AsSpan().IndexOfAny('.', 'e', 'E') >= 0)
        {
            // Python's float() of the literal: correctly rounded, overflowing to infinity. .NET Core 3.0+ parses
            // IEEE-correctly and also overflows to infinity rather than throwing.
            var value = double.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture);
            return FormatFloat(value, json);
        }

        // int(raw): JSON forbids leading zeros, so the digits are already canonical, except that "-0" is 0.
        // BigInteger normalises that and any other oddity from a value built in code.
        return BigInteger.Parse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)
            .ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// <c>repr(float)</c>, or <c>json.dumps</c> of a float when <paramref name="json"/> is true (which only
    /// differs for NaN and the infinities).
    /// </summary>
    internal static string FormatFloat(double value, bool json)
    {
        if (double.IsNaN(value)) return json ? "NaN" : "nan";
        if (double.IsPositiveInfinity(value)) return json ? "Infinity" : "inf";
        if (double.IsNegativeInfinity(value)) return json ? "-Infinity" : "-inf";
        if (value == 0) return double.IsNegative(value) ? "-0.0" : "0.0";

        // Shortest round-trip digits and decimal-point position from .NET's "R" format, which is either
        // fixed ("123.456") or scientific ("1.5E+300").
        var r = Math.Abs(value).ToString("R", CultureInfo.InvariantCulture);
        int exponent = 0;
        int ePos = r.IndexOf('E');
        var mantissa = r;
        if (ePos >= 0)
        {
            exponent = int.Parse(r.AsSpan(ePos + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            mantissa = r[..ePos];
        }

        int dot = mantissa.IndexOf('.');
        var intPart = dot >= 0 ? mantissa[..dot] : mantissa;
        var fracPart = dot >= 0 ? mantissa[(dot + 1)..] : string.Empty;
        var digits = intPart + fracPart;
        int decpt = intPart.Length + exponent; // digits before the decimal point

        int lead = 0;
        while (lead < digits.Length - 1 && digits[lead] == '0') lead++;
        digits = digits[lead..];
        decpt -= lead;
        digits = digits.TrimEnd('0');

        var sb = new System.Text.StringBuilder(32);
        if (double.IsNegative(value)) sb.Append('-');

        // CPython format_float_short, mode 'r': scientific when decpt <= -4 or decpt > 16.
        if (decpt <= -4 || decpt > 16)
        {
            sb.Append(digits[0]);
            if (digits.Length > 1) sb.Append('.').Append(digits, 1, digits.Length - 1);
            int e = decpt - 1;
            sb.Append('e').Append(e < 0 ? '-' : '+');
            sb.Append(Math.Abs(e).ToString("00", CultureInfo.InvariantCulture));
        }
        else if (decpt <= 0)
        {
            sb.Append("0.").Append('0', -decpt).Append(digits);
        }
        else if (decpt >= digits.Length)
        {
            sb.Append(digits).Append('0', decpt - digits.Length).Append(".0");
        }
        else
        {
            sb.Append(digits, 0, decpt).Append('.').Append(digits, decpt, digits.Length - decpt);
        }

        return sb.ToString();
    }
}
