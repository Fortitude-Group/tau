using System.Text.Json.Serialization;

namespace Tau.Contract;

/// <summary>
/// The body of a <c>POST /v1/systemone</c> response, exactly as defined by the pinned contract
/// snapshot. Contains no field outside the contract (Principle XIV: no silent extensions).
/// </summary>
public sealed class DecisionResponse
{
    /// <summary>The resolved Tau model id that answered the request.</summary>
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    /// <summary>One answer per question, keyed by the question's name, in the request's question order.</summary>
    [JsonPropertyName("answers")]
    public required OrderedDictionary<string, Answer> Answers { get; init; }

    /// <summary>Token usage for the request.</summary>
    [JsonPropertyName("usage")]
    public required Usage Usage { get; init; }
}
