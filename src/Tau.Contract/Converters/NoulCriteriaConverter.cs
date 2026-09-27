using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Tau.Contract.Converters;

/// <summary>
/// Reads and writes the optional <c>criteria</c> object on a <see cref="NoulQuestion"/>,
/// normalising its keys to lower case (the reference accepts <c>true</c>/<c>false</c> in any
/// letter case but treats them case-insensitively).
/// </summary>
public sealed class NoulCriteriaConverter : JsonConverter<IReadOnlyDictionary<string, JsonNode?>?>
{
    /// <inheritdoc />
    public override IReadOnlyDictionary<string, JsonNode?>? Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        var node = JsonNode.Parse(ref reader);
        if (node is not JsonObject criteria)
        {
            throw new JsonException("criteria must be a JSON object");
        }

        var normalised = new Dictionary<string, JsonNode?>(criteria.Count);
        foreach (var (key, value) in criteria)
        {
            // Later duplicates (after lower-casing) win; RequestValidator has already accepted
            // the input by the time this converter runs, so this is a pragmatic last-one-wins
            // resolution rather than a rule the contract itself specifies.
            normalised[key.ToLowerInvariant()] = value;
        }

        return normalised;
    }

    /// <inheritdoc />
    public override void Write(
        Utf8JsonWriter writer, IReadOnlyDictionary<string, JsonNode?>? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        foreach (var (key, node) in value)
        {
            writer.WritePropertyName(key);
            if (node is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                node.WriteTo(writer, options);
            }
        }

        writer.WriteEndObject();
    }
}
