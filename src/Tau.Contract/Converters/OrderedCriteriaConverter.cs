using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Tau.Contract.Converters;

/// <summary>
/// Reads and writes a <c>criteria</c> JSON object as an order-preserving list of key/value pairs,
/// used for <see cref="ChoiceQuestion.Criteria"/> where option order is the rendering order.
/// </summary>
public sealed class OrderedCriteriaConverter : JsonConverter<IReadOnlyList<KeyValuePair<string, JsonNode?>>>
{
    /// <inheritdoc />
    public override IReadOnlyList<KeyValuePair<string, JsonNode?>> Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var node = JsonNode.Parse(ref reader);
        if (node is not JsonObject criteria)
        {
            throw new JsonException("criteria must be a JSON object");
        }

        var list = new List<KeyValuePair<string, JsonNode?>>(criteria.Count);
        foreach (var (key, value) in criteria)
        {
            list.Add(new KeyValuePair<string, JsonNode?>(key, value));
        }

        return list;
    }

    /// <inheritdoc />
    public override void Write(
        Utf8JsonWriter writer, IReadOnlyList<KeyValuePair<string, JsonNode?>> value, JsonSerializerOptions options)
    {
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
