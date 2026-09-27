using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Tau.Contract;
using Tau.Inference.Engine;
using Tau.Runtime.Telemetry;

namespace Tau.Runtime;

/// <summary>Maps the HTTP surface: <c>POST /v1/systemone</c> plus the Tau-only discovery and health routes.</summary>
public static class SystemOneEndpoints
{
    /// <summary>Registers the routes.</summary>
    /// <param name="app">The application.</param>
    public static void MapTau(this WebApplication app)
    {
        app.MapPost("/v1/systemone", HandleAsync);
        app.MapGet("/v1/models", (IDecisionEngine engine) => Results.Json(
            new { models = engine.Models, aliases = engine.Aliases }, DiscoveryJson));
        app.MapGet("/healthz", (IDecisionEngine engine) =>
            engine.Ready ? Results.Ok(new { status = "ready" }) : Results.Json(new { status = "loading" }, statusCode: 503));
    }

    private static readonly JsonSerializerOptions DiscoveryJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private static async Task HandleAsync(HttpContext ctx, IDecisionEngine engine, TauTelemetry telemetry, TauOptions options)
    {
        var sw = Stopwatch.StartNew();
        using var activity = TauTelemetry.Source.StartActivity("systemone");

        // Enforce the body cap here as well as in Kestrel: other hosts (and chunked bodies) bypass Kestrel's check.
        var body = await ReadBoundedAsync(ctx, options.MaxRequestBytes);
        if (body is null)
        {
            await WriteProblemsAsync(ctx, telemetry, StatusCodes.Status413PayloadTooLarge,
                [new ValidationProblem("$", $"request body exceeds the {options.MaxRequestBytes}-byte limit")]);
            return;
        }

        if (!ContractParser.TryParse(body, out var request, out var problems) || request is null)
        {
            await WriteProblemsAsync(ctx, telemetry, StatusCodes.Status422UnprocessableEntity, problems);
            return;
        }

        var raw = ctx.Request.Headers.TryGetValue("x-tau-raw", out var rawHeader)
                  && bool.TryParse(rawHeader.ToString(), out var r) && r;
        DecisionResult result;
        try
        {
            result = await engine.DecideAsync(request, new DecisionOptions(raw), ctx.RequestAborted);
        }
        catch (DecisionRejectedException e)
        {
            await WriteProblemsAsync(ctx, telemetry, StatusCodes.Status422UnprocessableEntity, e.Problems);
            return;
        }

        var d = result.Diagnostics;
        var h = ctx.Response.Headers;
        h["x-tau-model-hash"] = d.ModelSha256;
        h["x-tau-calibrators"] = d.Calibrators.Count == 0 ? "none" : string.Join(",", d.Calibrators);
        h["x-tau-truncated"] = d.Truncated ? "true" : "false";
        h["x-tau-route-reason"] = AsciiHeader(d.RouteReason);
        h["x-tau-model-ms"] = d.ModelMilliseconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        activity?.SetTag("tau.model", d.ModelId);
        activity?.SetTag("tau.batch_rows", d.BatchRows);

        ctx.Response.StatusCode = StatusCodes.Status200OK;
        ctx.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(ctx.Response.Body, result.Response, ContractJson.Options, ctx.RequestAborted);
        telemetry.Answered(d, sw.Elapsed.TotalMilliseconds);
    }

    /// <summary>Reads the body as UTF-8, or returns null if it's longer than <paramref name="max"/> bytes.</summary>
    private static async Task<string?> ReadBoundedAsync(HttpContext ctx, long max)
    {
        if (ctx.Request.ContentLength > max) return null;
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        try
        {
            int n;
            while ((n = await ctx.Request.Body.ReadAsync(chunk, ctx.RequestAborted)) > 0)
            {
                if (buffer.Length + n > max) return null;
                buffer.Write(chunk, 0, n);
            }
        }
        catch (BadHttpRequestException e) when (e.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return null;
        }
        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private static async Task WriteProblemsAsync(HttpContext ctx, TauTelemetry telemetry, int status, IReadOnlyList<ValidationProblem> problems)
    {
        telemetry.Rejected(status);
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        var body = new ValidationErrorBody
        {
            Error = new ValidationErrorPayload
            {
                Message = problems.Count == 1 ? $"{problems[0].Path}: {problems[0].Problem}" : $"{problems.Count} validation problems",
                Details = problems,
            },
        };
        await JsonSerializer.SerializeAsync(ctx.Response.Body, body, ContractJson.Options, ctx.RequestAborted);
    }

    /// <summary>HTTP header values must be ASCII; route reasons can quote non-Latin text.</summary>
    private static string AsciiHeader(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s) sb.Append(c is >= ' ' and <= '~' ? c : '?');
        return sb.ToString();
    }

    /// <summary>Applies the request body limit to Kestrel and to each request.</summary>
    /// <param name="app">The application.</param>
    /// <param name="maxBytes">Limit.</param>
    public static void UseBodyLimit(this WebApplication app, long maxBytes) => app.Use(async (ctx, next) =>
    {
        if (ctx.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } f) f.MaxRequestBodySize = maxBytes;
        await next();
    });
}
