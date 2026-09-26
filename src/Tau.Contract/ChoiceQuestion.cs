using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Tau.Contract.Converters;

namespace Tau.Contract;

/// <summary>
/// A question that offers a fixed set of named options, one of which the model selects.
/// </summary>
public sealed class ChoiceQuestion : Question
{
    /// <inheritdoc />
    [JsonIgnore]
    public override string Type => "choice";

    /// <summary>
    /// The option key and description, in the order they were declared (option order is
    /// rendering order, 1 to 255 entries).
    /// </summary>
    [JsonPropertyName("criteria")]
    [JsonConverter(typeof(OrderedCriteriaConverter))]
    public required IReadOnlyList<KeyValuePair<string, JsonNode?>> Criteria { get; init; }
}
