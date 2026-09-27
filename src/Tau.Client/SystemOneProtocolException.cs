using System.Text.Json.Nodes;

namespace Tau.Client;

/// <summary>
/// Thrown when a <c>/v1/systemone</c> server returns a success status, but the body cannot be
/// used as promised — it does not parse as a <see cref="Tau.Contract.DecisionResponse"/>, or a
/// typed helper (<see cref="SystemOneClientExtensions.DecideAsync{TEnum}(ISystemOneClient,JsonNode?,string,IReadOnlyDictionary{TEnum,string}?,string,CancellationToken)"/>,
/// <see cref="SystemOneClientExtensions.ScoreAsync(ISystemOneClient,JsonNode?,string,IReadOnlyList{string},string,CancellationToken)"/>,
/// <see cref="SystemOneClientExtensions.NoulAsync(ISystemOneClient,JsonNode?,string,string?,string?,string,CancellationToken)"/>) cannot find the answer it asked for.
/// </summary>
public sealed class SystemOneProtocolException : Exception
{
    /// <summary>Creates a new <see cref="SystemOneProtocolException"/>.</summary>
    /// <param name="message">A description of what was expected and what was received instead.</param>
    public SystemOneProtocolException(string message)
        : base(message)
    {
    }

    /// <summary>Creates a new <see cref="SystemOneProtocolException"/> wrapping the parsing failure that caused it.</summary>
    /// <param name="message">A description of what was expected and what was received instead.</param>
    /// <param name="innerException">The underlying parsing failure.</param>
    public SystemOneProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
