using System.Text.Json.Serialization;

namespace Tau.Contract;

/// <summary>The answer to a <see cref="ChoiceQuestion"/>.</summary>
public sealed class ChoiceAnswer : Answer
{
    /// <inheritdoc />
    [JsonIgnore]
    public override string Type => "choice";

    /// <summary>The selected option's key (the highest-probability option).</summary>
    [JsonPropertyName("choice")]
    public required string Choice { get; init; }

    /// <summary>Each option's probability, in the order the question declared its options. Sums to 1 within rounding.</summary>
    [JsonPropertyName("probabilities")]
    public required OrderedDictionary<string, double> Probabilities { get; init; }

    /// <summary>A confidence figure derived from the probabilities.</summary>
    [JsonPropertyName("confidence")]
    public required double Confidence { get; init; }
}
