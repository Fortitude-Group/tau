using System.Text.Json.Nodes;
using Json.Schema;
using Tau.Contract;

namespace Tau.Conformance;

/// <summary>
/// The two JsonSchema.Net evaluators checked against every <c>/v1/systemone</c> response, both
/// built from the single embedded contract schema (<see cref="ContractSchemas.ResponseSchemaText"/>)
/// so there is exactly one source of truth for the shape (constitution Principle XIV).
/// </summary>
internal static class ResponseSchemas
{
    /// <summary>The contract schema exactly as pinned: no field outside the contract is permitted
    /// anywhere. This is what Tau's own responses are checked against.</summary>
    public static JsonSchema Strict { get; } = JsonSchema.FromText(ContractSchemas.ResponseSchemaText);

    /// <summary>The same schema with every closed <c>"additionalProperties": false</c> relaxed, so a
    /// peer server may legitimately extend the wire shape without failing structurally. Used for a
    /// peer server's responses (and for the primary target under <c>--lenient-target</c>).</summary>
    public static JsonSchema Lenient { get; } = JsonSchema.FromText(BuildLenientSchemaText());

    private static string BuildLenientSchemaText()
    {
        var root = JsonNode.Parse(ContractSchemas.ResponseSchemaText) as JsonObject
            ?? throw new InvalidOperationException("embedded response schema failed to parse");
        StripClosedAdditionalProperties(root);

        // JsonSchema.Net registers every built schema globally by its "$id"; the lenient variant
        // needs its own id so building it doesn't collide with the already-registered strict one.
        if (root["$id"] is JsonValue id && id.TryGetValue<string>(out var idText))
        {
            root["$id"] = idText + ".lenient";
        }

        return root.ToJsonString();
    }

    private static void StripClosedAdditionalProperties(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj["additionalProperties"] is JsonValue v && v.TryGetValue<bool>(out var closed) && !closed)
                {
                    obj.Remove("additionalProperties");
                }

                foreach (var (_, child) in obj.ToArray())
                {
                    StripClosedAdditionalProperties(child);
                }

                break;

            case JsonArray arr:
                foreach (var child in arr.ToArray())
                {
                    StripClosedAdditionalProperties(child);
                }

                break;
        }
    }
}
