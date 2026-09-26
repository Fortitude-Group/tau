using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tau.Inference.Text;

/// <summary>
/// Reproduces CPython's <c>str()</c> of what <c>json.loads</c> returned for a JSON text, byte for byte, and
/// Von's <c>_format_state</c> built on it.
/// </summary>
/// <remarks>
/// <para>
/// Containers print their items with <c>repr</c>: <c>{'k': 'v', 'n': 1}</c>, <c>[1, 'a']</c>, <c>True</c>,
/// <c>False</c>, <c>None</c>. A string inside a container uses Python's quoting (single quotes unless the
/// string has a single quote and no double quote), escapes <c>\\</c>, the chosen quote, <c>\n</c>, <c>\r</c>
/// and <c>\t</c>, and writes other non-printable code points as <c>\xNN</c>, <c>\uNNNN</c> or
/// <c>\UNNNNNNNN</c>. "Printable" follows the reference interpreter's Unicode database
/// (<see cref="PyUnicodePrintable"/>), not .NET's, because the two ship different Unicode versions.
/// </para>
/// <para>Numbers print as in <see cref="PyNumber"/>, with <c>inf</c>/<c>-inf</c> for overflowed floats.</para>
/// </remarks>
public static class PyRepr
{
    /// <summary><c>str(value)</c>: a top-level string is returned as it is; everything else as its repr.</summary>
    /// <param name="node">The parsed JSON value. Null is Python <c>None</c>.</param>
    /// <returns>The exact text CPython would produce.</returns>
    public static string Str(JsonNode? node)
    {
        if (node is JsonValue v && v.GetValueKind() == JsonValueKind.String)
        {
            return v.GetValue<string>();
        }

        var sb = new StringBuilder();
        WriteRepr(sb, node);
        return sb.ToString();
    }

    /// <summary><c>repr(value)</c>.</summary>
    /// <param name="node">The parsed JSON value. Null is Python <c>None</c>.</param>
    /// <returns>The exact text CPython would produce.</returns>
    public static string Repr(JsonNode? node)
    {
        var sb = new StringBuilder();
        WriteRepr(sb, node);
        return sb.ToString();
    }

    /// <summary>
    /// Von's <c>_format_state</c> (von-sdk 1.2.3): a string is returned as it is; an object becomes one
    /// <c>f"{k}: {v}"</c> line per key joined with <c>\n</c> (so a string value is printed raw and a nested
    /// container in repr style); anything else is <see cref="Str"/>.
    /// </summary>
    /// <param name="node">The parsed state.</param>
    /// <returns>The text Von feeds to the tokeniser.</returns>
    public static string FormatVonState(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            var sb = new StringBuilder();
            bool first = true;
            foreach (var (key, child) in obj)
            {
                if (!first) sb.Append('\n');
                first = false;
                sb.Append(key).Append(": ").Append(Str(child));
            }

            return sb.ToString();
        }

        return Str(node);
    }

    private static void WriteRepr(StringBuilder sb, JsonNode? node)
    {
        switch (node)
        {
            case null:
                sb.Append("None");
                return;

            case JsonObject obj:
                sb.Append('{');
                bool first = true;
                foreach (var (key, child) in obj)
                {
                    if (!first) sb.Append(", ");
                    first = false;
                    WriteStringRepr(sb, key);
                    sb.Append(": ");
                    WriteRepr(sb, child);
                }

                sb.Append('}');
                return;

            case JsonArray arr:
                sb.Append('[');
                for (int i = 0; i < arr.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    WriteRepr(sb, arr[i]);
                }

                sb.Append(']');
                return;

            case JsonValue value:
                switch (value.GetValueKind())
                {
                    case JsonValueKind.String:
                        WriteStringRepr(sb, value.GetValue<string>());
                        return;
                    case JsonValueKind.Number:
                        sb.Append(PyNumber.Format(value, json: false));
                        return;
                    case JsonValueKind.True:
                        sb.Append("True");
                        return;
                    case JsonValueKind.False:
                        sb.Append("False");
                        return;
                    case JsonValueKind.Null:
                        sb.Append("None");
                        return;
                    default:
                        throw new ArgumentException($"Unsupported JSON value kind {value.GetValueKind()}.", nameof(node));
                }

            default:
                throw new ArgumentException($"Unsupported JSON node type {node.GetType().Name}.", nameof(node));
        }
    }

    // CPython Objects/unicodeobject.c unicode_repr.
    private static void WriteStringRepr(StringBuilder sb, string s)
    {
        char quote = '\'';
        if (s.Contains('\'') && !s.Contains('"'))
        {
            quote = '"';
        }

        sb.Append(quote);
        for (int i = 0; i < s.Length; i++)
        {
            int cp = s[i];
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                cp = char.ConvertToUtf32(s[i], s[i + 1]);
                i++;
            }

            if (cp == quote || cp == '\\')
            {
                sb.Append('\\').Append((char)cp);
            }
            else if (cp == '\t')
            {
                sb.Append("\\t");
            }
            else if (cp == '\n')
            {
                sb.Append("\\n");
            }
            else if (cp == '\r')
            {
                sb.Append("\\r");
            }
            else if (cp < ' ' || cp == 0x7F)
            {
                sb.Append("\\x").Append(cp.ToString("x2", CultureInfo.InvariantCulture));
            }
            else if (cp < 0x7F)
            {
                sb.Append((char)cp);
            }
            else if (PyUnicodePrintable.IsPrintable(cp))
            {
                AppendCodePoint(sb, cp);
            }
            else if (cp <= 0xFF)
            {
                sb.Append("\\x").Append(cp.ToString("x2", CultureInfo.InvariantCulture));
            }
            else if (cp <= 0xFFFF)
            {
                sb.Append("\\u").Append(cp.ToString("x4", CultureInfo.InvariantCulture));
            }
            else
            {
                sb.Append("\\U").Append(cp.ToString("x8", CultureInfo.InvariantCulture));
            }
        }

        sb.Append(quote);
    }

    private static void AppendCodePoint(StringBuilder sb, int cp)
    {
        if (cp <= 0xFFFF)
        {
            sb.Append((char)cp);
        }
        else
        {
            sb.Append(char.ConvertFromUtf32(cp));
        }
    }
}
