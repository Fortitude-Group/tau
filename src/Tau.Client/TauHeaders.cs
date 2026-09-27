namespace Tau.Client;

/// <summary>
/// The Tau Runtime's optional <c>x-tau-*</c> request headers, for
/// <see cref="ISystemOneClient.SystemOneAsync(Tau.Contract.DecisionRequest, IReadOnlyDictionary{string, string}?, CancellationToken)"/>.
/// Servers that don't know them ignore them.
/// </summary>
public static class TauHeaders
{
    /// <summary>Request header: <c>true</c> skips Tau's calibrators and returns the model's own post-processing.</summary>
    public const string Raw = "x-tau-raw";

    /// <summary>
    /// Request header: <see cref="FullPrecision"/> asks for every value in the answers unrounded. The Runtime echoes it
    /// on the response when it honoured it; an endpoint that doesn't send it back rounded as usual (4 dp).
    /// </summary>
    public const string Precision = "x-tau-precision";

    /// <summary>The <see cref="Precision"/> value for unrounded answers.</summary>
    public const string FullPrecision = "full";
}
