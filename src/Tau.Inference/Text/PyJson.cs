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
        Write(sb, node);
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

    private static void Write(StringBuilder sb, JsonNode? node)
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
                foreach (var (key, child) in obj)
                {
                    if (!first) sb.Append(", ");
                    first = false;
                    WriteString(sb, key);
                    sb.Append(": ");
                    Write(sb, child);
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
                    Write(sb, arr[i]);
                }

                sb.Append(']');
                return;

            case JsonValue value:
                switch (value.GetValueKind())
                {
                    case JsonValueKind.String:
                        WriteString(sb, value.GetValue<string>());
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
    private static void WriteString(StringBuilder sb, string s)
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
                    if (c < 0x20)
                    {
                        sb.Append("\\u00").Append(HexLower((c >> 4) & 0xF)).Append(HexLower(c & 0xF));
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

    private static char HexLower(int nibble) => (char)(nibble < 10 ? '0' + nibble : 'a' + nibble - 10);
}
