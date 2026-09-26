using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Tau.Contract.Converters;

namespace Tau.Contract;

/// <summary>
/// The body of a <c>POST /v1/systemone</c> request, exactly as defined by the pinned contract
/// snapshot (<c>contracts/systemone/2026-09-27/</c>).
/// </summary>
/// <remarks>
/// A value produced by <see cref="ContractParser"/> has already passed
/// <see cref="Validation.RequestValidator"/>, so its shape is guaranteed to satisfy the contract.
/// Constructing one directly (for example via <c>JsonSerializer.Deserialize</c>) bypasses that
/// guarantee and is the caller's responsibility. (De)serialisation is handled entirely by
/// <see cref="DecisionRequestConverter"/>, which merges <see cref="Extensions"/> back into the
/// top-level object rather than nesting it under a property of its own.
/// </remarks>
[JsonConverter(typeof(DecisionRequestConverter))]
public sealed class DecisionRequest
{
    /// <summary>The requested Tau model id, or an auto-routing alias (see R-09). Wire name: <c>model</c>.</summary>
    public required string Model { get; init; }

    /// <summary>
    /// The content to evaluate: text, structured data, or JSON <c>null</c>. Wire name: <c>state</c>.
    /// <see cref="DecisionRequestConverter"/> throws while reading a body that omits the
    /// <c>state</c> key entirely, distinguishing "key absent" from "key present with a null value".
    /// </summary>
    public required JsonNode? State { get; init; }

    /// <summary>
    /// The named questions to answer, in the order they appeared in the request body. Order is
    /// significant: it sets batch order, not the meaning of any individual answer. Wire name: <c>questions</c>.
    /// </summary>
    public required OrderedDictionary<string, Question> Questions { get; init; }

    /// <summary>
    /// Top-level <c>x-tau-*</c> extension fields captured verbatim. Populated only for requests
    /// that have already passed validation (any other unknown top-level field is a 422).
    /// </summary>
    public JsonObject? Extensions { get; init; }
}
