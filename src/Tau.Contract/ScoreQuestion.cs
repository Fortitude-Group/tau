using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Tau.Contract;

/// <summary>
/// A question that asks the model to place its answer on an ordered scale of 2 to 10 levels.
/// </summary>
public sealed class ScoreQuestion : Question
{
    /// <inheritdoc />
    [JsonIgnore]
    public override string Type => "score";

    /// <summary>
    /// The scale's levels, in order, index 0 first (2 to 10 entries).
    /// </summary>
    [JsonPropertyName("criteria")]
    public required IReadOnlyList<JsonNode> Criteria { get; init; }
}
