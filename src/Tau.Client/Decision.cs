using Tau.Contract;

namespace Tau.Client;

/// <summary>The result of <see cref="SystemOneClientExtensions.DecideAsync{TEnum}(ISystemOneClient,System.Text.Json.Nodes.JsonNode?,string,IReadOnlyDictionary{TEnum,string}?,string,CancellationToken)"/>.</summary>
/// <typeparam name="TEnum">The enum type whose members are the question's options.</typeparam>
/// <param name="Value">The chosen enum member (the option the server gave the highest probability).</param>
/// <param name="Probabilities">Each enum member's probability, summing to 1 within rounding.</param>
/// <param name="Confidence">A confidence figure derived from the probabilities.</param>
/// <param name="Raw">The underlying <see cref="ChoiceAnswer"/> the server returned.</param>
public sealed record Decision<TEnum>(
    TEnum Value,
    IReadOnlyDictionary<TEnum, double> Probabilities,
    double Confidence,
    ChoiceAnswer Raw)
    where TEnum : struct, Enum;
