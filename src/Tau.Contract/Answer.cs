using System.Text.Json.Serialization;

namespace Tau.Contract;

/// <summary>
/// The answer to a single question inside a <see cref="DecisionResponse"/>, discriminated on the
/// wire by its <c>type</c> field (<c>choice</c>, <c>score</c> or <c>noul</c>). Each concrete
/// answer serialises exactly the fields the contract defines for its type, and no others.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(ChoiceAnswer), "choice")]
[JsonDerivedType(typeof(ScoreAnswer), "score")]
[JsonDerivedType(typeof(NoulAnswer), "noul")]
public abstract class Answer
{
    /// <summary>The wire discriminator for this answer (<c>"choice"</c>, <c>"score"</c> or <c>"noul"</c>).</summary>
    /// <remarks>
    /// The discriminator itself is written and read by the polymorphic serialiser, not by this
    /// property; the property exists purely so calling code can inspect the answer's kind
    /// without a type test.
    /// </remarks>
    [JsonIgnore]
    public abstract string Type { get; }
}
