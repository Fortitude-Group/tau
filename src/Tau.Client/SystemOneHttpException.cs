using System.Net;

namespace Tau.Client;

/// <summary>
/// Thrown when a <c>/v1/systemone</c> server returns a non-success status that isn't a
/// validation failure — for example 401 (missing/invalid key), 429 (rate limited, after retries
/// are exhausted), 529 (overloaded, after retries are exhausted), or any 5xx.
/// </summary>
public sealed class SystemOneHttpException : Exception
{
    /// <summary>The HTTP status the server returned.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>The raw response body, for diagnostics.</summary>
    public string Body { get; }

    /// <summary>Creates a new <see cref="SystemOneHttpException"/>.</summary>
    /// <param name="statusCode">The HTTP status the server returned.</param>
    /// <param name="body">The raw response body.</param>
    public SystemOneHttpException(HttpStatusCode statusCode, string body)
        : base($"/v1/systemone returned {(int)statusCode} {statusCode}.")
    {
        StatusCode = statusCode;
        Body = body;
    }
}
