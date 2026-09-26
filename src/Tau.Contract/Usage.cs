using System.Text.Json.Serialization;

namespace Tau.Contract;

/// <summary>Token usage reported alongside a <see cref="DecisionResponse"/>.</summary>
public sealed class Usage
{
    /// <summary>The tokens actually processed by the model (sum of attention masks over the request's rows).</summary>
    [JsonPropertyName("input_tokens")]
    public required int InputTokens { get; init; }

    /// <summary>Always 0: nothing is generated, matching the reference Laya implementation.</summary>
    [JsonPropertyName("output_tokens")]
    public required int OutputTokens { get; init; }
}
