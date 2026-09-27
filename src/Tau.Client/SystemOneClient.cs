using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Tau.Contract;

namespace Tau.Client;

/// <summary>
/// A typed client for any server implementing the pinned <c>/v1/systemone</c> contract.
/// </summary>
/// <remarks>
/// Two ways to construct one: hand it an <see cref="HttpClient"/> you already own and configured
/// (base address, headers, handler chain) — this client never disposes it; or hand it a base URL
/// and it creates and owns its own <see cref="HttpClient"/>, disposed when this client is.
/// </remarks>
public sealed class SystemOneClient : ISystemOneClient, IDisposable
{
    private const string RequestPath = "v1/systemone";

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly RetryPolicy _retryPolicy;

    /// <summary>
    /// Creates a client that sends requests through an <see cref="HttpClient"/> the caller owns.
    /// Its <see cref="HttpClient.BaseAddress"/> is used as-is (it must end with <c>/</c> for the
    /// request path to combine correctly) and this client never disposes it.
    /// </summary>
    /// <param name="http">The <see cref="HttpClient"/> to send requests through.</param>
    /// <param name="retryPolicy">
    /// The retry policy for transient (429/529) responses. Defaults to <see cref="RetryPolicy.Default"/>.
    /// </param>
    public SystemOneClient(HttpClient http, RetryPolicy? retryPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
        _ownsHttpClient = false;
        _retryPolicy = retryPolicy ?? RetryPolicy.Default;
    }

    /// <summary>
    /// Creates a client that owns its own <see cref="HttpClient"/>, pointed at
    /// <paramref name="baseUrl"/>. Disposing this client disposes that <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="baseUrl">
    /// The server's base URL (for example a local Tau Runtime, or TypeSafe's hosted endpoint). A
    /// missing trailing <c>/</c> is added automatically so the request path combines correctly.
    /// </param>
    /// <param name="apiKey">
    /// An API key sent as <c>Authorization: Bearer &lt;key&gt;</c>. Tau ignores this header
    /// (authentication is out of scope for the Runtime); Jev requires it. Omitted entirely when
    /// <c>null</c> or empty.
    /// </param>
    /// <param name="retryPolicy">
    /// The retry policy for transient (429/529) responses. Defaults to <see cref="RetryPolicy.Default"/>.
    /// </param>
    public SystemOneClient(Uri baseUrl, string? apiKey = null, RetryPolicy? retryPolicy = null)
        : this(baseUrl, new HttpClientHandler(), apiKey, retryPolicy)
    {
    }

    /// <summary>
    /// Test-only seam: as <see cref="SystemOneClient(Uri,string?,RetryPolicy?)"/>, but sending
    /// requests through a caller-supplied <see cref="HttpMessageHandler"/> instead of the real
    /// network, so the base-URL and header wiring above can be tested without a live server.
    /// </summary>
    internal SystemOneClient(Uri baseUrl, HttpMessageHandler handler, string? apiKey = null, RetryPolicy? retryPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);
        ArgumentNullException.ThrowIfNull(handler);
        var address = baseUrl.AbsoluteUri.EndsWith('/') ? baseUrl : new Uri(baseUrl.AbsoluteUri + "/");
        _http = new HttpClient(handler) { BaseAddress = address };
        if (!string.IsNullOrEmpty(apiKey))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        _ownsHttpClient = true;
        _retryPolicy = retryPolicy ?? RetryPolicy.Default;
    }

    /// <inheritdoc />
    public async Task<DecisionResponse> SystemOneAsync(DecisionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var json = JsonSerializer.Serialize(request, ContractJson.Options);
        var maxAttempts = Math.Max(1, _retryPolicy.MaxAttempts);

        for (var attempt = 1; ; attempt++)
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, RequestPath)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };

            using var response = await _http.SendAsync(httpRequest, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return ParseResponse(body);
            }

            var statusCode = (int)response.StatusCode;
            var isTransient = statusCode is 429 or 529;

            if (isTransient && attempt < maxAttempts)
            {
                var delay = _retryPolicy.BaseDelay * Math.Pow(2, attempt - 1);
                await _retryPolicy.DelayAsync(delay, ct).ConfigureAwait(false);
                continue;
            }

            if (statusCode == 422)
            {
                throw BuildValidationException(body);
            }

            throw new SystemOneHttpException(response.StatusCode, body);
        }
    }

    private static DecisionResponse ParseResponse(string body)
    {
        try
        {
            var response = JsonSerializer.Deserialize<DecisionResponse>(body, ContractJson.Options);
            if (response is null)
            {
                throw new SystemOneProtocolException("the /v1/systemone response body was JSON 'null'.");
            }

            return response;
        }
        catch (JsonException ex)
        {
            throw new SystemOneProtocolException(
                $"the /v1/systemone response body did not parse as a valid response: {ex.Message}", ex);
        }
        catch (NotSupportedException ex)
        {
            throw new SystemOneProtocolException(
                $"the /v1/systemone response body did not parse as a valid response: {ex.Message}", ex);
        }
    }

    private static SystemOneValidationException BuildValidationException(string body)
    {
        try
        {
            var errorBody = JsonSerializer.Deserialize<ValidationErrorBody>(body, ContractJson.Options);
            if (errorBody is { Error.Details.Count: > 0 })
            {
                return new SystemOneValidationException(errorBody.Error.Details, body);
            }
        }
        catch (JsonException)
        {
            // Not Tau's validation-error shape (for example a Jev or Kev 422 body). Fall through
            // and report the raw body instead of throwing here.
        }

        return new SystemOneValidationException([new ValidationProblem("$", body)], body);
    }

    /// <summary>Disposes the internally-owned <see cref="HttpClient"/>, if this client created one.</summary>
    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }
}
