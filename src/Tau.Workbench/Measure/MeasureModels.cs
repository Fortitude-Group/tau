using Tau.Workbench.Spec;

namespace Tau.Workbench.Measure;

/// <summary>The three measurement phases.</summary>
public static class Phases
{
    /// <summary>Uncalibrated reference output (<c>x-tau-raw: true</c>).</summary>
    public const string Raw = "raw";

    /// <summary>The endpoint's own calibrated output (no <c>x-tau-raw</c>), after the Runtime has loaded calibrators.</summary>
    public const string Calibrated = "calibrated";

    /// <summary>The Workbench's own application of the fitted calibrator to raw probabilities (no endpoint call).</summary>
    public const string Offline = "offline";
}

/// <summary>
/// The precision of a phase's stored probabilities. The Workbench asks for <c>x-tau-precision: full</c> on every
/// request; a Tau Runtime honours it, other endpoints (Jev, Kev) round to 4 dp regardless.
/// </summary>
public static class Precisions
{
    /// <summary>Every answer was unrounded (the endpoint echoed <c>x-tau-precision: full</c>).</summary>
    public const string Full = "full";

    /// <summary>No answer was unrounded: the endpoint rounded them.</summary>
    public const string Rounded = "rounded";

    /// <summary>Some answers were unrounded and some weren't.</summary>
    public const string Mixed = "mixed";

    /// <summary>The precision of a phase from how many of its answered calls honoured full precision.</summary>
    /// <param name="honoured">Answered calls that echoed <c>x-tau-precision: full</c>.</param>
    /// <param name="answered">Answered calls.</param>
    public static string Of(int honoured, int answered) =>
        answered > 0 && honoured == answered ? Full : honoured == 0 ? Rounded : Mixed;

    /// <summary>
    /// What a phase's precision means for calibrators fitted on it, or null when they were fitted on unrounded values.
    /// A null precision is a run recorded before the Workbench asked for full precision, so it was rounded.
    /// </summary>
    /// <param name="precision">The raw phase's precision.</param>
    public static string? CalibratorNote(string? precision) => precision switch
    {
        Full => null,
        Mixed => "Some probabilities were rounded to 4 dp by the endpoint, so the calibrators were fitted partly on rounded values.",
        Rounded => "Probabilities rounded to 4 dp by the endpoint; calibrators fitted on rounded values.",
        _ => "Probabilities rounded to 4 dp by the endpoint (measured before the Workbench asked for x-tau-precision: full); calibrators fitted on rounded values. Re-measure with 'tau run --force' to fit on unrounded values.",
    };
}

/// <summary>A failed call, excluded from metrics.</summary>
/// <param name="Status">HTTP status, or 0 for a connection failure or timeout.</param>
/// <param name="Body">The response body or error message (truncated to 2,000 characters).</param>
public sealed record MeasureError(int Status, string Body);

/// <summary>One line of <c>runs/&lt;model&gt;/&lt;split&gt;.&lt;phase&gt;.jsonl</c>.</summary>
public sealed record MeasuredItem
{
    /// <summary>The item id.</summary>
    public required string ItemId { get; init; }

    /// <summary>The gold label.</summary>
    public required string Gold { get; init; }

    /// <summary>The argmax class label, or null on failure.</summary>
    public string? Answer { get; init; }

    /// <summary>The full probability distribution in class order, or null on failure.</summary>
    public OrderedDictionary<string, double>? Probabilities { get; init; }

    /// <summary>max(p), the confidence used for ECE and thresholds.</summary>
    public double? ConfidenceMaxp { get; init; }

    /// <summary>Whether argmax equals gold.</summary>
    public bool? Correct { get; init; }

    /// <summary>Wall-clock latency of the call in milliseconds (0 for offline records).</summary>
    public double LatencyMs { get; init; }

    /// <summary>The Runtime's reported model time (<c>x-tau-model-ms</c>), when present.</summary>
    public double? ModelMs { get; init; }

    /// <summary>Whether the Runtime truncated the state (<c>x-tau-truncated</c>), when reported.</summary>
    public bool? Truncated { get; init; }

    /// <summary>The Runtime's applied calibrators (<c>x-tau-calibrators</c>), when reported.</summary>
    public string? Calibrators { get; init; }

    /// <summary>The Runtime's <c>x-tau-model-hash</c>, when reported.</summary>
    public string? ModelHash { get; init; }

    /// <summary>The failure, when the call failed.</summary>
    public MeasureError? Error { get; init; }

    /// <summary>
    /// Returns a copy holding <paramref name="vector"/> as its distribution, with the answer (argmax),
    /// confidence (max p) and correctness derived from it.
    /// </summary>
    /// <param name="question">The question (for class labels).</param>
    /// <param name="vector">Probabilities in class order.</param>
    public MeasuredItem WithVector(QuestionSpec question, double[] vector)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(vector);
        var classes = question.Classes;
        var probs = new OrderedDictionary<string, double>(classes.Count);
        for (int i = 0; i < classes.Count; i++)
        {
            probs[classes[i]] = vector[i];
        }

        int arg = MetricSet.ArgMax(vector);
        return this with
        {
            Probabilities = probs,
            Answer = classes[arg],
            ConfidenceMaxp = vector[arg],
            Correct = question.ClassIndex(Gold) == arg,
            Error = null,
        };
    }

    /// <summary>The probability vector in the question's class order, or null for a failed item.</summary>
    /// <param name="question">The question.</param>
    public double[]? Vector(QuestionSpec question)
    {
        ArgumentNullException.ThrowIfNull(question);
        if (Probabilities is null)
        {
            return null;
        }

        var classes = question.Classes;
        var v = new double[classes.Count];
        for (int i = 0; i < v.Length; i++)
        {
            if (!Probabilities.TryGetValue(classes[i], out v[i]))
            {
                return null;
            }
        }

        return v;
    }
}

/// <summary>Latency percentiles in milliseconds.</summary>
/// <param name="P50">Median.</param>
/// <param name="P95">95th percentile.</param>
/// <param name="Mean">Mean.</param>
public sealed record LatencyStats(double P50, double P95, double Mean);

/// <summary>Mean GPU power over a phase.</summary>
/// <param name="MeanWatts">Mean <c>power.draw</c> across samples.</param>
/// <param name="Samples">Number of samples.</param>
/// <param name="IntervalMs">Sampling interval.</param>
/// <param name="Gpu">GPU name.</param>
/// <param name="Note">What the number includes.</param>
public sealed record GpuPower(double MeanWatts, int Samples, int IntervalMs, string? Gpu, string Note);

/// <summary>The summary written next to each measurement file.</summary>
public sealed record MeasureSummary
{
    /// <summary>The model id.</summary>
    public required string Model { get; init; }

    /// <summary>calibration or heldout.</summary>
    public required string Split { get; init; }

    /// <summary>raw, calibrated or offline.</summary>
    public required string Phase { get; init; }

    /// <summary>The endpoint base URL (null for offline).</summary>
    public string? Endpoint { get; init; }

    /// <summary>Who answered.</summary>
    public EndpointIdentity? EndpointIdentity { get; init; }

    /// <summary>The model's ONNX sha256 from <c>/v1/models</c>, when the endpoint is Tau.</summary>
    public string? ModelHash { get; init; }

    /// <summary>Distinct <c>x-tau-model-hash</c> values seen in responses.</summary>
    public IReadOnlyList<string> ModelHashesSeen { get; init; } = [];

    /// <summary>Distinct <c>x-tau-calibrators</c> values seen in responses.</summary>
    public IReadOnlyList<string> CalibratorsSeen { get; init; } = [];

    /// <summary>
    /// Whether the stored probabilities are unrounded: <see cref="Precisions.Full"/>,
    /// <see cref="Precisions.Rounded"/> or <see cref="Precisions.Mixed"/>. Null in runs recorded before
    /// the Workbench asked for full precision, which were rounded.
    /// </summary>
    public string? Precision { get; init; }

    /// <summary>Items sent.</summary>
    public required int Items { get; init; }

    /// <summary>Items with a usable answer (the metrics' N).</summary>
    public required int Measured { get; init; }

    /// <summary>Items that failed and are excluded from the metrics.</summary>
    public required int Failures { get; init; }

    /// <summary>Failures by HTTP status (0 = connection failure or timeout).</summary>
    public IReadOnlyDictionary<string, int> FailuresByStatus { get; init; } = new Dictionary<string, int>();

    /// <summary>Items the Runtime reported as truncated.</summary>
    public int Truncated { get; init; }

    /// <summary>The metrics over measured items, or null when none succeeded.</summary>
    public MetricSet? Metrics { get; init; }

    /// <summary>What the metrics score against: the dataset's gold labels or the frontier model's answers.</summary>
    public ReferenceKind Reference { get; init; } = ReferenceKind.Gold;

    /// <summary>How confidence is defined (FR-009).</summary>
    public string ConfidenceNote { get; init; } = MetricSet.ConfidenceNote;

    /// <summary>What the exclusion count means.</summary>
    public string ExclusionNote { get; init; } = "";

    /// <summary>Wall latency per call.</summary>
    public LatencyStats? LatencyMs { get; init; }

    /// <summary>Reported model time per call, when the endpoint reports it.</summary>
    public LatencyStats? ModelMs { get; init; }

    /// <summary>Wall-clock duration of the whole phase in seconds.</summary>
    public double DurationSeconds { get; init; }

    /// <summary>Concurrent requests in flight.</summary>
    public int Concurrency { get; init; }

    /// <summary>GPU power during the phase, when nvidia-smi was available.</summary>
    public GpuPower? Gpu { get; init; }

    /// <summary>Why GPU power is missing, when it is.</summary>
    public string? GpuNote { get; init; }

    /// <summary>When the phase started (UTC, ISO 8601).</summary>
    public required string StartedUtc { get; init; }
}
