using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using Tau.Calibration;
using Tau.Contract;
using Tau.Workbench.Data;
using Tau.Workbench.Reference;
using Tau.Workbench.Spec;

namespace Tau.Workbench.Measure;

/// <summary>Knobs for a measurement phase.</summary>
public sealed record MeasureOptions
{
    /// <summary>Requests in flight at once (default 4).</summary>
    public int Concurrency { get; init; } = 4;

    /// <summary>
    /// Starts a GPU power sampler for the phase, or returns null with a reason. Defaults to nvidia-smi;
    /// tests pass a stub or a function that returns null.
    /// </summary>
    public Func<(GpuPowerSampler? Sampler, string? Reason)> StartGpuSampler { get; init; } =
        () => (GpuPowerSampler.TryStartNvidiaSmi(out var reason), reason);

    /// <summary>The clock for the recorded start time.</summary>
    public Func<DateTimeOffset> Clock { get; init; } = () => DateTimeOffset.UtcNow;

    /// <summary>The labels the summary scores against; null resolves them from the spec.</summary>
    public ReferenceLabels? Reference { get; init; }
}

/// <summary>
/// The measure stage (T023): sends every item of a split to a <c>/v1/systemone</c> endpoint, records the
/// full probability distribution per item in item order, and summarises accuracy, ECE on max(p), Brier,
/// log loss (and MAE for score questions) through Tau.Calibration.
/// </summary>
public static class MeasureStage
{
    /// <summary>Builds the contract request for one item, pinned to <paramref name="model"/>.</summary>
    /// <param name="question">The spec's question.</param>
    /// <param name="model">The model id.</param>
    /// <param name="text">The item text (the request's state).</param>
    public static DecisionRequest BuildRequest(QuestionSpec question, string model, string text)
    {
        ArgumentNullException.ThrowIfNull(question);
        Question q = question.Type switch
        {
            QuestionType.Choice => new ChoiceQuestion
            {
                Instructions = JsonValue.Create(question.Instructions),
                Criteria = question.Options.Select(o => new KeyValuePair<string, JsonNode?>(o.Key, JsonValue.Create(o.Description))).ToArray(),
            },
            QuestionType.Score => new ScoreQuestion
            {
                Instructions = JsonValue.Create(question.Instructions),
                Criteria = question.Options.Select(o => (JsonNode)JsonValue.Create(o.Description)).ToArray(),
            },
            _ => new NoulQuestion
            {
                Instructions = JsonValue.Create(question.Instructions),
                Criteria = question.Options.Count == 0 ? null : question.Options.ToDictionary(o => o.Key, o => (JsonNode?)JsonValue.Create(o.Description)),
            },
        };

        var questions = new OrderedDictionary<string, Question> { [question.Key] = q };
        return new DecisionRequest { Model = model, State = JsonValue.Create(text), Questions = questions };
    }

    /// <summary>
    /// Reads the probability vector for the spec's question out of a response: choice in option-key order,
    /// score by level "0".."k-1", noul as [1 − p, p].
    /// </summary>
    /// <param name="question">The spec's question.</param>
    /// <param name="response">The response.</param>
    /// <param name="problem">Why no vector could be read, when null is returned.</param>
    public static double[]? ExtractProbabilities(QuestionSpec question, DecisionResponse response, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(response);
        problem = null;
        if (!response.Answers.TryGetValue(question.Key, out var answer))
        {
            problem = $"the response has no answer for question '{question.Key}'";
            return null;
        }

        double[] v;
        switch (answer)
        {
            case ChoiceAnswer c when question.Type == QuestionType.Choice:
                v = ByKey(question.Classes, c.Probabilities, out problem);
                break;
            case ScoreAnswer s when question.Type == QuestionType.Score:
                v = ByKey(question.Classes, s.Probabilities, out problem);
                break;
            case NoulAnswer n when question.Type == QuestionType.Noul:
                v = [1 - n.Noul, n.Noul];
                break;
            default:
                problem = $"the answer is of type '{answer.Type}' but the question is '{question.Type.ToWireString()}'";
                return null;
        }

        if (problem is null && v.Any(p => !double.IsFinite(p) || p < 0 || p > 1))
        {
            problem = "a probability is not a finite number between 0 and 1";
        }

        return problem is null ? v : null;
    }

    /// <summary>
    /// Measures one split for one model in one phase and writes <c>&lt;split&gt;.&lt;phase&gt;.jsonl</c> plus its
    /// summary. Failed items are recorded with their error and excluded from the metrics, with the count stated.
    /// </summary>
    /// <param name="spec">The decision spec.</param>
    /// <param name="model">The model id.</param>
    /// <param name="split">calibration or heldout.</param>
    /// <param name="phase"><see cref="Phases.Raw"/> or <see cref="Phases.Calibrated"/>.</param>
    /// <param name="items">The split's items, in file order.</param>
    /// <param name="endpoint">The endpoint connection.</param>
    /// <param name="identity">The endpoint's identity (from <see cref="WorkbenchEndpoint.IdentifyAsync"/>).</param>
    /// <param name="options">Concurrency, GPU sampling and clock.</param>
    /// <param name="ct">Cancellation.</param>
    /// <exception cref="StageBlockedException">
    /// The calibrated phase ran against a Tau Runtime that applied no calibrator to any item.
    /// </exception>
    public static async Task<MeasureSummary> RunAsync(
        DecisionSpec spec, string model, string split, string phase, IReadOnlyList<DatasetItem> items,
        WorkbenchEndpoint endpoint, EndpointIdentity identity, MeasureOptions? options = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(identity);
        if (phase is not (Phases.Raw or Phases.Calibrated))
        {
            throw new ArgumentException($"phase must be '{Phases.Raw}' or '{Phases.Calibrated}'.", nameof(phase));
        }

        options ??= new MeasureOptions();
        var reference = options.Reference ?? ReferenceLabels.Load(spec);
        reference.Require(split, items);
        var started = options.Clock();
        var (sampler, gpuReason) = options.StartGpuSampler();
        var records = new MeasuredItem[items.Count];
        var wall = Stopwatch.StartNew();
        using (var gate = new SemaphoreSlim(Math.Max(1, options.Concurrency)))
        {
            var tasks = items.Select(async (item, index) =>
            {
                await gate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    var call = await endpoint.DecideAsync(BuildRequest(spec.Question, model, item.Text), phase == Phases.Raw, ct).ConfigureAwait(false);
                    records[index] = ToRecord(spec.Question, item, call);
                }
                finally
                {
                    gate.Release();
                }
            }).ToArray();
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        var duration = wall.Elapsed.TotalSeconds;
        GpuPower? gpu = sampler is null ? null : await sampler.StopAsync().ConfigureAwait(false);
        if (sampler is not null)
        {
            await sampler.DisposeAsync().ConfigureAwait(false);
        }

        var calibratorsSeen = records.Where(r => r.Error is null && r.Calibrators is not null).Select(r => r.Calibrators!).Distinct().Order(StringComparer.Ordinal).ToArray();
        if (phase == Phases.Calibrated && identity.IsTau && records.Any(r => r.Error is null)
            && calibratorsSeen.All(c => string.Equals(c, "none", StringComparison.OrdinalIgnoreCase)))
        {
            throw new StageBlockedException(
                $"The calibrated phase for '{model}' got answers with no calibrator applied (x-tau-calibrators: none). Restart the Runtime with Tau:CalibratorsDirectory={spec.CalibratorsRoot}, then run 'tau measure <spec> --phase calibrated'.");
        }

        // The records keep the dataset's label; the summary scores against the reference.
        var summary = Summarise(spec, model, split, phase, reference.Apply(spec.Question, split, records)) with
        {
            Reference = reference.Kind,
            Endpoint = endpoint.BaseUrl.AbsoluteUri,
            EndpointIdentity = identity,
            ModelHash = identity.HashOf(model),
            ModelHashesSeen = records.Where(r => r.ModelHash is not null).Select(r => r.ModelHash!).Distinct().Order(StringComparer.Ordinal).ToArray(),
            CalibratorsSeen = calibratorsSeen,
            DurationSeconds = duration,
            Concurrency = Math.Max(1, options.Concurrency),
            Gpu = gpu,
            GpuNote = gpu is null ? gpuReason ?? "No GPU power sample was taken during the phase." : null,
            StartedUtc = started.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        };
        WorkbenchJson.WriteJsonl(spec.RunPath(model, split, phase), records);
        WorkbenchJson.WriteJson(spec.RunSummaryPath(model, split, phase), summary);
        return summary;
    }

    /// <summary>Builds the summary over a set of records (metrics over successful items only).</summary>
    /// <param name="spec">The decision spec.</param>
    /// <param name="model">The model id.</param>
    /// <param name="split">The split.</param>
    /// <param name="phase">The phase.</param>
    /// <param name="records">The records.</param>
    public static MeasureSummary Summarise(DecisionSpec spec, string model, string split, string phase, IReadOnlyList<MeasuredItem> records)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(records);
        var ok = records.Where(r => r.Error is null && r.Vector(spec.Question) is not null).ToArray();
        var failures = records.Where(r => r.Error is not null).ToArray();
        var metrics = MetricSet.Compute(
            ok.Select(r => r.Vector(spec.Question)!).ToArray(),
            ok.Select(r => spec.Question.ClassIndex(r.Gold)!.Value).ToArray(),
            spec.Question.Type == QuestionType.Score);
        var latencies = ok.Select(r => r.LatencyMs).Where(l => l > 0).ToArray();
        var modelMs = ok.Where(r => r.ModelMs is not null).Select(r => r.ModelMs!.Value).ToArray();
        return new MeasureSummary
        {
            Model = model,
            Split = split,
            Phase = phase,
            Items = records.Count,
            Measured = ok.Length,
            Failures = failures.Length,
            FailuresByStatus = failures.GroupBy(f => f.Error!.Status.ToString(CultureInfo.InvariantCulture))
                .OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count()),
            Truncated = ok.Count(r => r.Truncated == true),
            Metrics = metrics,
            ExclusionNote = failures.Length == 0
                ? "No item failed; the metrics cover every item."
                : $"{failures.Length} of {records.Count} items failed (errors are in the .jsonl) and are excluded; the metrics cover the other {ok.Length}.",
            LatencyMs = Stats(latencies),
            ModelMs = Stats(modelMs),
            StartedUtc = "",
        };
    }

    private static double[] ByKey(IReadOnlyList<string> classes, OrderedDictionary<string, double> probabilities, out string? problem)
    {
        problem = null;
        var v = new double[classes.Count];
        for (int i = 0; i < classes.Count; i++)
        {
            if (!probabilities.TryGetValue(classes[i], out v[i]))
            {
                problem = $"the answer has no probability for '{classes[i]}'";
                return v;
            }
        }

        return v;
    }

    private static LatencyStats? Stats(IReadOnlyList<double> values) =>
        values.Count == 0 ? null : new LatencyStats(MetricSet.Percentile(values, 50)!.Value, MetricSet.Percentile(values, 95)!.Value, values.Average());

    private static MeasuredItem ToRecord(QuestionSpec question, DatasetItem item, EndpointCall call)
    {
        var baseRecord = new MeasuredItem
        {
            ItemId = item.Id,
            Gold = item.Label,
            LatencyMs = Math.Round(call.LatencyMs, 3),
            ModelMs = call.ModelMs,
            Truncated = call.Truncated,
            Calibrators = call.Calibrators,
            ModelHash = call.ModelHash,
        };
        if (call.Response is null)
        {
            return baseRecord with { Error = new MeasureError(call.FailureStatus ?? 0, call.FailureBody ?? "") };
        }

        var v = ExtractProbabilities(question, call.Response, out var problem);
        if (v is null)
        {
            return baseRecord with { Error = new MeasureError(200, "unusable answer: " + problem) };
        }

        return baseRecord.WithVector(question, v);
    }
}
