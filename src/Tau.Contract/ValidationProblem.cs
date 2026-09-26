using System.Text.Json.Serialization;

namespace Tau.Contract;

/// <summary>
/// A single reason a request failed validation, naming the JSON path of the offending field.
/// </summary>
/// <param name="Path">
/// A dotted JSON path to the offending field (for example <c>questions.urgency.criteria</c>), or
/// <c>"$"</c> for a problem with the request body as a whole (malformed JSON, or a body that
/// isn't a JSON object).
/// </param>
/// <param name="Problem">A human-readable description of what is wrong.</param>
public sealed record ValidationProblem(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("problem")] string Problem);
