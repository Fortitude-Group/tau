using Tau.Contract;

namespace Tau.Client;

/// <summary>The result of <see cref="SystemOneClientExtensions.NoulDetailedAsync"/>.</summary>
/// <param name="Value">The probability that the statement is true, on a scale from 0 (no) to 1 (yes).</param>
/// <param name="Raw">The underlying <see cref="NoulAnswer"/> the server returned.</param>
public sealed record NoulResult(double Value, NoulAnswer Raw);
