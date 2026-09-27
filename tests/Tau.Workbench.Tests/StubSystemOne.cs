using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Tau.Workbench.Tests;

/// <summary>
/// A fake <c>/v1/systemone</c> server as an <see cref="HttpMessageHandler"/>: answers from a probability
/// function of the item text, optionally lists models at <c>/v1/models</c>, sets Tau's diagnostic headers,
/// and can fail chosen items. It records every request's headers.
/// </summary>
internal sealed class StubSystemOne : HttpMessageHandler
{
    public const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    public Func<string, bool, double[]> Probabilities { get; init; } = (_, _) => [0.7, 0.1, 0.1, 0.1];

    public Func<string, HttpResponseMessage?> Fail { get; init; } = _ => null;

    public bool IsTau { get; init; } = true;

    public string Calibrators { get; init; } = "none";

    public bool Truncate { get; init; }

    public int MaxDelayMs { get; init; }

    /// <summary>Echo x-tau-precision: full when asked, as a Tau Runtime does (Jev and Kev don't).</summary>
    public bool HonoursPrecision { get; init; }

    /// <summary>Whether each request asked for x-tau-precision: full.</summary>
    public ConcurrentBag<bool> PrecisionRequested { get; } = [];

    public ConcurrentBag<(string Text, bool Raw)> Calls { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (request.Method == HttpMethod.Get && path.EndsWith("/v1/models", StringComparison.Ordinal))
        {
            if (!IsTau)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            var models = new JsonObject
            {
                ["models"] = new JsonArray(
                    new JsonObject { ["id"] = "model-a", ["family"] = "laya", ["revision"] = "rev1", ["onnx_sha256"] = Hash, ["loaded"] = true },
                    new JsonObject { ["id"] = "model-b", ["family"] = "von", ["revision"] = "rev2", ["onnx_sha256"] = new string('b', 64), ["loaded"] = true }),
                ["aliases"] = new JsonArray("auto"),
            };
            return Json(models);
        }

        var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false))!.AsObject();
        string text = (string)body["state"]!;
        bool raw = request.Headers.TryGetValues("x-tau-raw", out var values) && values.Contains("true");
        Calls.Add((text, raw));
        bool full = request.Headers.TryGetValues("x-tau-precision", out var precision) && precision.Contains("full");
        PrecisionRequested.Add(full);
        if (MaxDelayMs > 0)
        {
            await Task.Delay(Math.Abs(text.GetHashCode(StringComparison.Ordinal)) % MaxDelayMs, cancellationToken).ConfigureAwait(false);
        }

        if (Fail(text) is { } failure)
        {
            return failure;
        }

        var (key, question) = body["questions"]!.AsObject().First();
        string type = (string)question!["type"]!;
        var p = Probabilities(text, raw);
        JsonObject answer;
        switch (type)
        {
            case "choice":
                var keys = question["criteria"]!.AsObject().Select(kv => kv.Key).ToArray();
                var probs = new JsonObject();
                foreach (var i in Enumerable.Range(0, keys.Length).Reverse())
                {
                    probs[keys[i]] = p[i]; // reversed on purpose: the Workbench must reorder by key
                }

                answer = new JsonObject { ["type"] = "choice", ["choice"] = keys[Array.IndexOf(p, p.Max())], ["probabilities"] = probs, ["confidence"] = 0.5 };
                break;
            case "score":
                var levels = question["criteria"]!.AsArray();
                var sp = new JsonObject();
                var legend = new JsonObject();
                for (int i = 0; i < levels.Count; i++)
                {
                    sp[i.ToString(CultureInfo.InvariantCulture)] = p[i];
                    legend[i.ToString(CultureInfo.InvariantCulture)] = (string)levels[i]!;
                }

                answer = new JsonObject { ["type"] = "score", ["score"] = 1.0, ["legend"] = legend, ["probabilities"] = sp, ["confidence"] = 0.5 };
                break;
            default:
                answer = new JsonObject { ["type"] = "noul", ["noul"] = p[1] };
                break;
        }

        var response = Json(new JsonObject
        {
            ["model"] = (string)body["model"]!,
            ["answers"] = new JsonObject { [key] = answer },
            ["usage"] = new JsonObject { ["input_tokens"] = 12, ["output_tokens"] = 0 },
        });
        if (IsTau)
        {
            response.Headers.Add("x-tau-model-ms", "3.25");
            response.Headers.Add("x-tau-truncated", Truncate ? "true" : "false");
            response.Headers.Add("x-tau-model-hash", Hash);
            response.Headers.Add("x-tau-calibrators", raw ? "none" : Calibrators);
            if (full && HonoursPrecision)
            {
                response.Headers.Add("x-tau-precision", "full");
            }
        }

        return response;
    }

    private static HttpResponseMessage Json(JsonNode node) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json"),
    };
}
