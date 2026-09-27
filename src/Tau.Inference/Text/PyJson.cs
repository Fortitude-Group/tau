using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tau.Inference.Text;

/// <summary>
/// Reproduces CPython's <c>json.dumps(value, ensure_ascii=False)</c> byte for byte, where <c>value</c> is what
/// <c>json.loads</c> returned for the same JSON text.
/// </summary>
/// <remarks>
/// <para>
/// Laya serialises structured state and criteria with <c>json.dumps</c> before tokenising, so a single differing
/// character here changes the token sequence. Output uses the default separators <c>", "</c> and <c>": "</c>,
/// keeps object key order, prints floats as <c>repr(float)</c> and integers exactly (see <see cref="PyNumber"/>),
/// passes non-ASCII through, and escapes only <c>"</c>, <c>\</c>, <c>\n</c>, <c>\r</c>, <c>\t</c>, <c>\b</c>,
/// <c>\f</c> and the remaining control characters below U+0020 (as lower-case <c>\u00xx</c>).
/// </para>
/// <para>
/// Laya's <c>render_criterion</c> uses <c>separators=(", ", ": "), default=str</c>. The separators are the
/// defaults and <c>default=str</c> never fires for a <c>json.loads</c> result, so it is also <see cref="Dumps"/>.
/// </para>
/// <para>
/// Von serialises structured instructions with <c>json.dumps(v, sort_keys=isinstance(v, dict))</c>, i.e. the
/// default <c>ensure_ascii=True</c>; that is <see cref="DumpsAscii"/>.
/// </para>
/// <para>
/// Numbers are classified as int or float by their original spelling, so pass nodes parsed from JSON text
/// (<see cref="JsonNode.Parse(string, JsonNodeOptions?, JsonDocumentOptions)"/>) rather than values rebuilt in
/// code, which lose it; see <see cref="PyNumber"/> for how code-built values are handled.
/// </para>
/// </remarks>
public static class PyJson
{
    /// <summary><c>json.dumps(value, ensure_ascii=False)</c>. A null node is JSON <c>null</c>.</summary>
    /// <param name="node">The parsed JSON value.</param>
    /// <returns>The exact text CPython would produce.</returns>
    public static string Dumps(JsonNode? node)
    {
        var sb = new StringBuilder();
        Write(sb, node, ascii: false, sortKeys: false);
        return sb.ToString();
    }

    /// <summary>
    /// <c>json.dumps(value, sort_keys=sortKeys)</c> with the default <c>ensure_ascii=True</c>: every character
    /// outside U+0020..U+007E is escaped (the usual short forms for controls, <c>\uxxxx</c> otherwise, astral
    /// characters as a surrogate pair). With <paramref name="sortKeys"/>, every object's keys are sorted by code
    /// point at every depth, as Python sorts strings.
    /// </summary>
    /// <param name="node">The parsed JSON value.</param>
    /// <param name="sortKeys">Sort object keys by code point.</param>
    /// <returns>The exact text CPython would produce.</returns>
    public static string DumpsAscii(JsonNode? node, bool sortKeys)
    {
        var sb = new StringBuilder();
        Write(sb, node, ascii: true, sortKeys);
        return sb.ToString();
    }

    /// <summary>
    /// Laya's <c>serialize_state</c> (and <c>render_criterion</c>): a string is returned as it is, unquoted;
    /// anything else goes through <see cref="Dumps"/>.
    /// </summary>
    /// <param name="node">The parsed JSON value.</param>
    /// <returns>The text Laya feeds to the tokeniser.</returns>
    public static string DumpsState(JsonNode? node)
    {
        if (node is JsonValue v && v.GetValueKind() == JsonValueKind.String)
        {
            return v.GetValue<string>();
        }

        return Dumps(node);
    }

    private static void Write(StringBuilder sb, JsonNode? node, bool ascii, bool sortKeys)
    {
        switch (node)
        {
            case null:
                sb.Append("null");
                return;

            case JsonObject obj:
                if (obj.Count == 0)
                {
                    sb.Append("{}");
                    return;
                }

                sb.Append('{');
                bool first = true;
                IEnumerable<KeyValuePair<string, JsonNode?>> entries =
                    sortKeys ? obj.OrderBy(kv => kv.Key, CodePointComparer.Instance) : obj;
                foreach (var (key, child) in entries)
                {
                    if (!first) sb.Append(", ");
                    first = false;
                    WriteString(sb, key, ascii);
                    sb.Append(": ");
                    Write(sb, child, ascii, sortKeys);
                }

                sb.Append('}');
                return;

            case JsonArray arr:
                if (arr.Count == 0)
                {
                    sb.Append("[]");
                    return;
                }

                sb.Append('[');
                for (int i = 0; i < arr.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    Write(sb, arr[i], ascii, sortKeys);
                }

                sb.Append(']');
                return;

            case JsonValue value:
                switch (value.GetValueKind())
                {
                    case JsonValueKind.String:
                        WriteString(sb, value.GetValue<string>(), ascii);
                        return;
                    case JsonValueKind.Number:
                        sb.Append(PyNumber.Format(value, json: true));
                        return;
                    case JsonValueKind.True:
                        sb.Append("true");
                        return;
                    case JsonValueKind.False:
                        sb.Append("false");
                        return;
                    case JsonValueKind.Null:
                        sb.Append("null");
                        return;
                    default:
                        throw new ArgumentException($"Unsupported JSON value kind {value.GetValueKind()}.", nameof(node));
                }

            default:
                throw new ArgumentException($"Unsupported JSON node type {node.GetType().Name}.", nameof(node));
        }
    }

    // CPython json.encoder: ESCAPE = r'[\x00-\x1f\\"\b\f\n\r\t]', other controls as '\\u{0:04x}'.
    // With ensure_ascii, ESCAPE_ASCII = r'([\\"]|[^\ -~])': everything outside ' '..'~' is escaped too, and a
    // UTF-16 code unit maps one-to-one onto Python's surrogate-pair escape for an astral character.
    private static void WriteString(StringBuilder sb, string s, bool ascii)
    {
        sb.Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (c < 0x20 || (ascii && c > 0x7E))
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        sb.Append('"');
    }

    /// <summary>Orders strings by Unicode code point, as Python compares <c>str</c>.</summary>
    private sealed class CodePointComparer : IComparer<string>
    {
        public static readonly CodePointComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            var a = (x ?? "").EnumerateRunes();
            var b = (y ?? "").EnumerateRunes();
            while (true)
            {
                var ha = a.MoveNext();
                var hb = b.MoveNext();
                if (!ha || !hb) return ha ? 1 : hb ? -1 : 0;
                var c = a.Current.Value.CompareTo(b.Current.Value);
                if (c != 0) return c;
            }
        }
    }
}
