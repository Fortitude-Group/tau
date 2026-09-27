using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Tau.Inference.Engine;
using Tau.Runtime;
using Tau.Runtime.Telemetry;

var builder = WebApplication.CreateBuilder(args);
var options = builder.Configuration.GetSection("Tau").Get<TauOptions>() ?? new TauOptions();
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<TauTelemetry>();
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = options.MaxRequestBytes);

// The engine owns model loading, routing, inference and calibration. Tests replace it.
builder.Services.AddTauEngine(options);

var otel = builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("tau-runtime", serviceVersion: "0.1.0"))
    .WithMetrics(m =>
    {
        m.AddMeter(TauTelemetry.Name).AddAspNetCoreInstrumentation().AddPrometheusExporter();
        if (options.OtlpEndpoint is { Length: > 0 } ep) m.AddOtlpExporter(o => o.Endpoint = new Uri(ep));
    })
    .WithTracing(t =>
    {
        t.AddSource(TauTelemetry.Name).AddAspNetCoreInstrumentation();
        if (options.OtlpEndpoint is { Length: > 0 } ep) t.AddOtlpExporter(o => o.Endpoint = new Uri(ep));
    });

var app = builder.Build();
app.UseBodyLimit(options.MaxRequestBytes);
app.MapTau();
app.MapPrometheusScrapingEndpoint("/metrics");

// Fail at start-up, not on the first request, if a model, provider or calibrator is wrong (FR-017, FR-019).
_ = app.Services.GetRequiredService<IDecisionEngine>();

app.Run();

/// <summary>Entry point, exposed for WebApplicationFactory tests.</summary>
public partial class Program;
