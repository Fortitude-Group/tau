using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Tau.Contract;

/// <summary>
/// A single question inside a <see cref="DecisionRequest"/>, discriminated on the wire by its
/// <c>type</c> field (<c>choice</c>, <c>score</c> or <c>noul</c>).
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(ChoiceQuestion), "choice")]
[JsonDerivedType(typeof(ScoreQuestion), "score")]
[JsonDerivedType(typeof(NoulQuestion), "noul")]
public abstract class Question
{
    /// <summary>The wire discriminator for this question (<c>"choice"</c>, <c>"score"</c> or <c>"noul"</c>).</summary>
    /// <remarks>
    /// The discriminator itself is written and read by the polymorphic serialiser, not by this
    /// property; the property exists purely so calling code can inspect the question's kind
    /// without a type test.
    /// </remarks>
    [JsonIgnore]
    public abstract string Type { get; }

    /// <summary>
    /// The instructions given to the model for this question, as text, an object or an array.
    /// </summary>
    [JsonPropertyName("instructions")]
    public required JsonNode Instructions { get; init; }
}
