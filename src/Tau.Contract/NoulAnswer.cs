using System.Text.Json.Serialization;

namespace Tau.Contract;

/// <summary>The answer to a <see cref="NoulQuestion"/>.</summary>
public sealed class NoulAnswer : Answer
{
    /// <inheritdoc />
    [JsonIgnore]
    public override string Type => "noul";

    /// <summary>The probability that the statement is true, on a scale from 0 (no) to 1 (yes).</summary>
    [JsonPropertyName("noul")]
    public required double Noul { get; init; }
}
