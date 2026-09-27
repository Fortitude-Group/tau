using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tau.Client;
using Tau.Contract;

namespace Tau.Workbench.Measure;

/// <summary>An installed model as reported by a Tau Runtime's <c>GET /v1/models</c>.</summary>
/// <param name="Id">Model id.</param>
/// <param name="Family">Model family.</param>
/// <param name="Revision">Pinned revision.</param>
/// <param name="OnnxSha256">sha256 of the ONNX file (the calibrator <c>modelHash</c>).</param>
/// <param name="Loaded">Whether it was resident.</param>
public sealed record EndpointModel(string Id, string? Family, string? Revision, string? OnnxSha256, bool? Loaded);

/// <summary>Who answered: a Tau Runtime (with its installed models) or some other contract endpoint.</summary>
/// <param name="IsTau">True when <c>GET /v1/models</c> answered in Tau's shape.</param>
/// <param name="Description">A one-line description for reports.</param>
/// <param name="Models">Installed models (empty unless Tau).</param>
/// <param name="Aliases">Auto-routing aliases (empty unless Tau).</param>
public sealed record EndpointIdentity(bool IsTau, string Description, IReadOnlyList<EndpointModel> Models, IReadOnlyList<string> Aliases)
{
    /// <summary>The description used when <c>/v1/models</c> is absent.</summary>
    public const string NotTau = "unknown (not a Tau endpoint)";

    /// <summary>The ONNX sha256 of an installed model, or null.</summary>
    /// <param name="model">Model id.</param>
    public string? HashOf(string model) => Models.FirstOrDefault(m => m.Id == model)?.OnnxSha256;
}

/// <summary>The outcome of one <c>/v1/systemone</c> call, success or failure, with Tau's diagnostics when present.</summary>
public sealed record EndpointCall
{
    /// <summary>The parsed response, or null on failure.</summary>
    public DecisionResponse? Response { get; init; }

    /// <summary>The failing HTTP status (0 for a connection failure or timeout), or null on success.</summary>
    public int? FailureStatus { get; init; }

    /// <summary>The failure body or message, or null on success.</summary>
    public string? FailureBody { get; init; }

    /// <summary>Wall-clock latency of the call, in milliseconds, including any client retries.</summary>
    public double LatencyMs { get; init; }

    /// <summary>The <c>x-tau-model-ms</c> header, when present.</summary>
    public double? ModelMs { get; init; }

    /// <summary>The <c>x-tau-truncated</c> header, when present.</summary>
    public bool? Truncated { get; init; }

    /// <summary>The <c>x-tau-model-hash</c> header, when present.</summary>
    public string? ModelHash { get; init; }

    /// <summary>The <c>x-tau-calibrators</c> header, when present.</summary>
    public string? Calibrators { get; init; }

    /// <summary>
    /// True when the endpoint echoed <c>x-tau-precision: full</c>, so the answer's values are unrounded; false when
    /// it didn't, so they are rounded as the reference rounds them.
    /// </summary>
    public bool FullPrecision { get; init; }
}

/// <summary>
/// The Workbench's connection to one <c>/v1/systemone</c> endpoint. Requests go through
/// <see cref="SystemOneClient"/> (Tau.Client); a <see cref="CaptureHandler"/> in the handler chain reads the
/// <c>x-tau-*</c> response headers, which the client itself does not expose. Every request asks for
/// <c>x-tau-precision: full</c>, so the stored probabilities are the values the Runtime computed, not the 4-dp
/// rounding, wherever the endpoint supports it.
/// </summary>
public sealed class WorkbenchEndpoint : IDisposable
{
    private static readonly IReadOnlyDictionary<string, string> RawHeaders = new Dictionary<string, string>
    {
        [TauHeaders.Raw] = "true",
        [TauHeaders.Precision] = TauHeaders.FullPrecision,
    };

    private static readonly IReadOnlyDictionary<string, string> CalibratedHeaders = new Dictionary<string, string>
    {
        [TauHeaders.Precision] = TauHeaders.FullPrecision,
    };
    private readonly HttpClient _http;
    private readonly SystemOneClient _client;

    /// <summary>Creates a connection.</summary>
    /// <param name="baseUrl">The endpoint's base URL.</param>
    /// <param name="handler">The innermost HTTP handler; null for a real network handler. Tests pass a stub.</param>
    /// <param name="retryPolicy">The client's retry policy for 429/529; null for the client default.</param>
    public WorkbenchEndpoint(Uri baseUrl, HttpMessageHandler? handler = null, RetryPolicy? retryPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);
        BaseUrl = baseUrl.AbsoluteUri.EndsWith('/') ? baseUrl : new Uri(baseUrl.AbsoluteUri + "/");
        _http = new HttpClient(new CaptureHandler { InnerHandler = handler ?? new SocketsHttpHandler() })
        {
            BaseAddress = BaseUrl,
            Timeout = TimeSpan.FromMinutes(2),
        };
        _client = new SystemOneClient(_http, retryPolicy);
    }

    /// <summary>The base URL (always ending in '/').</summary>
    public Uri BaseUrl { get; }

    /// <summary>
    /// Identifies the endpoint through <c>GET /v1/models</c>. Anything other than a 200 in Tau's shape
    /// means "not a Tau endpoint", which is fine for measuring but rules out fitting calibrators.
    /// </summary>
    /// <param name="ct">Cancellation.</param>
    public async Task<EndpointIdentity> IdentifyAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.GetAsync("v1/models", ct).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return new EndpointIdentity(false, EndpointIdentity.NotTau, [], []);
            }

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (JsonNode.Parse(body) is not JsonObject obj || obj["models"] is not JsonArray models)
            {
                return new EndpointIdentity(false, EndpointIdentity.NotTau, [], []);
            }

            var list = models.OfType<JsonObject>().Select(m => new EndpointModel(
                Str(m["id"]) ?? "", Str(m["family"]), Str(m["revision"]), Str(m["onnx_sha256"]),
                m["loaded"] is JsonValue l && l.TryGetValue<bool>(out var b) ? b : null)).ToArray();
            var aliases = obj["aliases"] is JsonArray a ? a.Select(Str).OfType<string>().ToArray() : [];
            return new EndpointIdentity(true, $"Tau Runtime at {BaseUrl} ({list.Length} model(s) installed)", list, aliases);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return new EndpointIdentity(false, EndpointIdentity.NotTau, [], []);
        }
    }

    /// <summary>Sends one decision request and captures the outcome. Never throws for an endpoint failure.</summary>
    /// <param name="request">The request.</param>
    /// <param name="raw">True to send <c>x-tau-raw: true</c> (uncalibrated reference output).</param>
    /// <param name="ct">Cancellation.</param>
    public async Task<EndpointCall> DecideAsync(DecisionRequest request, bool raw, CancellationToken ct = default)
    {
        var capture = new ExchangeCapture();
        CaptureHandler.Current = capture;
        var sw = Stopwatch.StartNew();
        try
        {
            var response = await _client.SystemOneAsync(request, raw ? RawHeaders : CalibratedHeaders, ct).ConfigureAwait(false);
            return Build(capture, sw.Elapsed.TotalMilliseconds) with { Response = response };
        }
        catch (SystemOneHttpException e)
        {
            return Build(capture, sw.Elapsed.TotalMilliseconds) with { FailureStatus = (int)e.StatusCode, FailureBody = Trim(e.Body) };
        }
        catch (SystemOneValidationException e)
        {
            return Build(capture, sw.Elapsed.TotalMilliseconds) with { FailureStatus = 422, FailureBody = Trim(e.RawBody) };
        }
        catch (SystemOneProtocolException e)
        {
            return Build(capture, sw.Elapsed.TotalMilliseconds) with { FailureStatus = capture.Status ?? 200, FailureBody = Trim("unusable response: " + e.Message) };
        }
        catch (HttpRequestException e)
        {
            return Build(capture, sw.Elapsed.TotalMilliseconds) with { FailureStatus = 0, FailureBody = Trim("connection failed: " + e.Message) };
        }
        catch (TaskCanceledException e) when (!ct.IsCancellationRequested)
        {
            return Build(capture, sw.Elapsed.TotalMilliseconds) with { FailureStatus = 0, FailureBody = Trim("timed out: " + e.Message) };
        }
        finally
        {
            CaptureHandler.Current = null;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _client.Dispose();
        _http.Dispose();
    }

    private static EndpointCall Build(ExchangeCapture c, double latency) => new()
    {
        LatencyMs = latency,
        ModelMs = double.TryParse(c.Header("x-tau-model-ms"), NumberStyles.Float, CultureInfo.InvariantCulture, out var ms) ? ms : null,
        Truncated = c.Header("x-tau-truncated") is { } t ? string.Equals(t, "true", StringComparison.OrdinalIgnoreCase) : null,
        ModelHash = c.Header("x-tau-model-hash"),
        Calibrators = c.Header("x-tau-calibrators"),
        FullPrecision = string.Equals(c.Header(TauHeaders.Precision)?.Trim(), TauHeaders.FullPrecision, StringComparison.OrdinalIgnoreCase),
    };

    private static string Trim(string s) => s.Length <= 2000 ? s : s[..2000] + "...";

    private static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}

/// <summary>Per-call state shared between <see cref="WorkbenchEndpoint"/> and <see cref="CaptureHandler"/>.</summary>
internal sealed class ExchangeCapture
{
    private readonly Dictionary<string, string> _responseHeaders = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The last response status seen.</summary>
    public int? Status { get; private set; }

    public string? Header(string name) => _responseHeaders.GetValueOrDefault(name);

    public void Record(HttpResponseMessage response)
    {
        Status = (int)response.StatusCode;
        _responseHeaders.Clear();
        foreach (var (name, values) in response.Headers)
        {
            _responseHeaders[name] = string.Join(",", values);
        }
    }
}

/// <summary>
/// A delegating handler that records the response headers for the call in flight (Tau.Client returns
/// only the parsed body). The call is identified through an <see cref="AsyncLocal{T}"/>, so concurrent calls never
/// see each other's headers.
/// </summary>
internal sealed class CaptureHandler : DelegatingHandler
{
    private static readonly AsyncLocal<ExchangeCapture?> Slot = new();

    public static ExchangeCapture? Current
    {
        get => Slot.Value;
        set => Slot.Value = value;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var capture = Slot.Value;
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        capture?.Record(response);
        return response;
    }
}
