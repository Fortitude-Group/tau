using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Tau.Contract.Converters;

/// <summary>
/// Reads and writes a <see cref="DecisionRequest"/>, merging its <c>x-tau-*</c> extension fields
/// back into the same top-level JSON object rather than nesting them under a property of their
/// own (which is what a generic <c>[JsonExtensionData]</c>-driven <see cref="JsonObject"/>
/// property would otherwise produce).
/// </summary>
public sealed class DecisionRequestConverter : JsonConverter<DecisionRequest>
{
    /// <inheritdoc />
    public override DecisionRequest Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var node = JsonNode.Parse(ref reader) as JsonObject
            ?? throw new JsonException("request body must be a JSON object");

        if (!node.TryGetPropertyValue("model", out var modelNode) || modelNode is not JsonValue modelValue
            || !modelValue.TryGetValue<string>(out var model))
        {
            throw new JsonException("model must be a string");
        }

        if (!node.TryGetPropertyValue("state", out var state))
        {
            throw new JsonException("state is required");
        }

        if (!node.TryGetPropertyValue("questions", out var questionsNode) || questionsNode is not JsonObject questionsObject)
        {
            throw new JsonException("questions must be an object");
        }

        var questions = new OrderedDictionary<string, Question>(questionsObject.Count);
        foreach (var (key, value) in questionsObject)
        {
            questions[key] = value.Deserialize<Question>(options)
                ?? throw new JsonException($"question '{key}' could not be read");
        }

        JsonObject? extensions = null;
        foreach (var (key, value) in node)
        {
            if (key is "model" or "state" or "questions")
            {
                continue;
            }

            extensions ??= [];
            extensions[key] = value?.DeepClone();
        }

        return new DecisionRequest
        {
            Model = model,
            State = state,
            Questions = questions,
            Extensions = extensions,
        };
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, DecisionRequest value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        writer.WriteString("model", value.Model);

        writer.WritePropertyName("state");
        if (value.State is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            value.State.WriteTo(writer, options);
        }

        writer.WritePropertyName("questions");
        writer.WriteStartObject();
        foreach (var (key, question) in value.Questions)
        {
            writer.WritePropertyName(key);
            JsonSerializer.Serialize(writer, question, options);
        }

        writer.WriteEndObject();

        if (value.Extensions is not null)
        {
            foreach (var (key, extension) in value.Extensions)
            {
                writer.WritePropertyName(key);
                if (extension is null)
                {
                    writer.WriteNullValue();
                }
                else
                {
                    extension.WriteTo(writer, options);
                }
            }
        }

        writer.WriteEndObject();
    }
}
