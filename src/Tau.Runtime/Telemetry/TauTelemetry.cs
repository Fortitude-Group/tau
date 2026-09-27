using System.Diagnostics;
using System.Diagnostics.Metrics;
using Tau.Inference.Engine;

namespace Tau.Runtime.Telemetry;

/// <summary>
/// Tau's OpenTelemetry instruments (FR-021). One histogram or counter per signal the spec names, tagged with
/// the model id, the model's ONNX hash prefix and the calibrators applied, so a latency figure can always be
/// traced back to the exact model that produced it.
/// </summary>
public sealed class TauTelemetry : IDisposable
{
    /// <summary>Meter and activity source name.</summary>
    public const string Name = "Tau.Runtime";

    private readonly Meter _meter = new(Name, "0.1.0");
    private readonly Histogram<double> _requestMs;
    private readonly Histogram<double> _modelMs;
    private readonly Counter<long> _inputTokens;
    private readonly Histogram<int> _batchRows;
    private readonly Counter<long> _requests;
    private readonly Counter<long> _truncations;

    /// <summary>Traces for each <c>/v1/systemone</c> request.</summary>
    public static readonly ActivitySource Source = new(Name, "0.1.0");

    /// <summary>Creates the instruments.</summary>
    public TauTelemetry()
    {
        _requestMs = _meter.CreateHistogram<double>("tau.request.duration", "ms", "Wall time of a /v1/systemone request, parse to response");
        _modelMs = _meter.CreateHistogram<double>("tau.model.duration", "ms", "Time in the model forward pass");
        _inputTokens = _meter.CreateCounter<long>("tau.input_tokens", "{token}", "Tokens processed by the model");
        _batchRows = _meter.CreateHistogram<int>("tau.batch.rows", "{row}", "Rows in a request's single forward pass");
        _requests = _meter.CreateCounter<long>("tau.requests", "{request}", "Requests by outcome");
        _truncations = _meter.CreateCounter<long>("tau.truncations", "{request}", "Requests whose state was truncated to fit");
    }

    /// <summary>Records an answered request.</summary>
    /// <param name="d">Diagnostics.</param>
    /// <param name="requestMs">Wall time.</param>
    public void Answered(DecisionDiagnostics d, double requestMs)
    {
        var tags = new TagList
        {
            { "model", d.ModelId },
            { "model_version", d.ModelSha256.Length >= 12 ? d.ModelSha256[..12] : d.ModelSha256 },
            { "calibrators", d.Calibrators.Count == 0 ? "none" : string.Join(",", d.Calibrators) },
        };
        _requestMs.Record(requestMs, tags);
        _modelMs.Record(d.ModelMilliseconds, tags);
        _inputTokens.Add(d.InputTokens, tags);
        _batchRows.Record(d.BatchRows, tags);
        _requests.Add(1, new TagList { { "outcome", "ok" }, { "model", d.ModelId } });
        if (d.Truncated) _truncations.Add(1, new TagList { { "model", d.ModelId } });
    }

    /// <summary>Records a rejected request.</summary>
    /// <param name="status">HTTP status returned.</param>
    public void Rejected(int status) => _requests.Add(1, new TagList { { "outcome", status.ToString(System.Globalization.CultureInfo.InvariantCulture) } });

    /// <inheritdoc />
    public void Dispose() => _meter.Dispose();
}
