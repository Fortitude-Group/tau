using Tau.Contract;

namespace Tau.Client;

/// <summary>The result of <see cref="SystemOneClientExtensions.ScoreAsync(ISystemOneClient,System.Text.Json.Nodes.JsonNode?,string,IReadOnlyList{string},string,CancellationToken)"/>.</summary>
/// <param name="Score">The probability-weighted answer across the levels; it can land between levels.</param>
/// <param name="Level">
/// <paramref name="Score"/> rounded to the nearest level index (away from zero, clamped to the
/// level range) — a convenient single answer when the caller wants one level, not a distribution.
/// </param>
/// <param name="Probabilities">Each level's probability, in level order, summing to 1 within rounding.</param>
/// <param name="Confidence">A confidence figure derived from the probabilities.</param>
/// <param name="Legend">Each level's rendered description, in level order, as the server echoed it.</param>
public sealed record ScoreResult(
    double Score,
    int Level,
    IReadOnlyList<double> Probabilities,
    double Confidence,
    IReadOnlyList<string> Legend)
{
    /// <summary>The underlying <see cref="ScoreAnswer"/> the server returned.</summary>
    public required ScoreAnswer Raw { get; init; }
}
