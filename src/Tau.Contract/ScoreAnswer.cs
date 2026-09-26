using System.Text.Json.Serialization;

namespace Tau.Contract;

/// <summary>The answer to a <see cref="ScoreQuestion"/>.</summary>
public sealed class ScoreAnswer : Answer
{
    /// <inheritdoc />
    [JsonIgnore]
    public override string Type => "score";

    /// <summary>The probability-weighted answer across the levels; it can land between levels.</summary>
    [JsonPropertyName("score")]
    public required double Score { get; init; }

    /// <summary>The level index ("0".."k-1"), keyed as a string, mapped to the level's rendered text.</summary>
    [JsonPropertyName("legend")]
    public required OrderedDictionary<string, string> Legend { get; init; }

    /// <summary>Each level's probability, keyed by its index ("0".."k-1") as a string.</summary>
    [JsonPropertyName("probabilities")]
    public required OrderedDictionary<string, double> Probabilities { get; init; }

    /// <summary>A confidence figure derived from the probabilities.</summary>
    [JsonPropertyName("confidence")]
    public required double Confidence { get; init; }
}
