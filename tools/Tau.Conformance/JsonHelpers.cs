using System.Text.Json.Nodes;

namespace Tau.Conformance;

/// <summary>Small, defensive readers over <see cref="JsonNode"/> that return <c>null</c> instead of
/// throwing when a value is absent or the wrong shape — every conformance check reports a missing
/// or malformed field as a finding, never as an unhandled exception.</summary>
internal static class JsonHelpers
{
    public static string? AsString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;

    public static double? AsDouble(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<double>(out var d) ? d : null;
}
