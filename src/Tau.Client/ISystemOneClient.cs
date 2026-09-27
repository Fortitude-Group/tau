using Tau.Contract;

namespace Tau.Client;

/// <summary>
/// A typed client for any server implementing the pinned <c>/v1/systemone</c> contract
/// (<c>contracts/systemone/2026-09-27/</c>) — the Tau Runtime, TypeSafe's hosted Jev, or Kev.
/// </summary>
public interface ISystemOneClient
{
    /// <summary>
    /// Sends a raw <see cref="DecisionRequest"/> to <c>POST /v1/systemone</c> and returns the
    /// parsed <see cref="DecisionResponse"/>.
    /// </summary>
    /// <param name="request">The request to send.</param>
    /// <param name="ct">A token that cancels the request.</param>
    /// <returns>The parsed response.</returns>
    /// <exception cref="SystemOneValidationException">
    /// The server rejected the request as invalid (HTTP 422).
    /// </exception>
    /// <exception cref="SystemOneHttpException">
    /// The server returned any other non-success status (for example 401, 429, 529 or a 5xx).
    /// </exception>
    /// <exception cref="SystemOneProtocolException">
    /// The server returned a success status, but the body did not parse as a
    /// <see cref="DecisionResponse"/>.
    /// </exception>
    Task<DecisionResponse> SystemOneAsync(DecisionRequest request, CancellationToken ct = default);

    /// <summary>
    /// As <see cref="SystemOneAsync(DecisionRequest, CancellationToken)"/>, with extra HTTP headers on this
    /// request only. Use it to send <c>x-tau-raw: true</c>, which asks the Tau Runtime for the model's
    /// uncalibrated reference probabilities (servers that don't know the header ignore it).
    /// </summary>
    /// <param name="request">The request to send.</param>
    /// <param name="headers">
    /// Request headers to add, by name; <c>null</c> or empty sends none. Content headers such as
    /// <c>Content-Type</c> can't be set this way.
    /// </param>
    /// <param name="ct">A token that cancels the request.</param>
    /// <returns>The parsed response.</returns>
    /// <exception cref="ArgumentException">A header name is empty, or a header name or value is invalid.</exception>
    /// <exception cref="SystemOneValidationException">
    /// The server rejected the request as invalid (HTTP 422).
    /// </exception>
    /// <exception cref="SystemOneHttpException">
    /// The server returned any other non-success status (for example 401, 429, 529 or a 5xx).
    /// </exception>
    /// <exception cref="SystemOneProtocolException">
    /// The server returned a success status, but the body did not parse as a
    /// <see cref="DecisionResponse"/>.
    /// </exception>
    Task<DecisionResponse> SystemOneAsync(
        DecisionRequest request, IReadOnlyDictionary<string, string>? headers, CancellationToken ct = default);
}
