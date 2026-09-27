using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Tau.Contract.Converters;

namespace Tau.Contract;

/// <summary>
/// A yes/no question: the model answers with a probability that the statement is true.
/// </summary>
public sealed class NoulQuestion : Question
{
    /// <inheritdoc />
    [JsonIgnore]
    public override string Type => "noul";

    /// <summary>
    /// Optional descriptions for the <c>true</c> and <c>false</c> outcomes. Keys are
    /// case-insensitive on the wire and normalised to lower case here, as the reference does.
    /// </summary>
    [JsonPropertyName("criteria")]
    [JsonConverter(typeof(NoulCriteriaConverter))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, JsonNode?>? Criteria { get; init; }
}
