using Tau.Workbench.Data;
using Tau.Workbench.Frontier;
using Tau.Workbench.Measure;
using Tau.Workbench.Spec;
using Tau.Workbench.Threshold;

namespace Tau.Workbench.Cascade;

/// <summary>The cascade simulation for one model: keep the local answer at or above τ, escalate the rest.</summary>
public sealed record CascadeResult
{
    /// <summary>The model id.</summary>
    public required string Model { get; init; }

    /// <summary>Which measurement the confidences came from.</summary>
    public required string Source { get; init; }

    /// <summary>The threshold, or null when none met the target.</summary>
    public double? Tau { get; init; }

    /// <summary>Why no cascade was simulated, when it wasn't.</summary>
    public string? NotSimulated { get; init; }

    /// <summary>Held-out items with a usable local answer.</summary>
    public required int Items { get; init; }

    /// <summary>Held-out items whose local measurement failed (excluded).</summary>
    public required int ExcludedFailures { get; init; }

    /// <summary>Items kept local (confidence ≥ τ).</summary>
    public int KeptLocal { get; init; }

    /// <summary>Items escalated to the frontier model.</summary>
    public int Escalated { get; init; }

    /// <summary>Escalated items with no cached frontier answer: counted, excluded from blended accuracy, never guessed.</summary>
    public int MissingFrontier { get; init; }

    /// <summary>Share of items kept local.</summary>
    public double? ShareLocal { get; init; }

    /// <summary>Share of items escalated.</summary>
    public double? ShareEscalated { get; init; }

    /// <summary>The local model alone on every item.</summary>
    public required CountedRate LocalOnlyAccuracy { get; init; }

    /// <summary>The frontier model alone, on items that have a frontier answer.</summary>
    public required CountedRate FrontierOnlyAccuracy { get; init; }

    /// <summary>The cascade, on items with a final answer.</summary>
    public CountedRate? BlendedAccuracy { get; init; }

    /// <summary>Items where a human override replaced the frontier answer.</summary>
    public int OverridesUsed { get; init; }

    /// <summary>Median wall latency of the local calls, milliseconds.</summary>
    public double? LocalLatencyP50Ms { get; init; }

    /// <summary>Which measurement the latency came from.</summary>
    public string? LatencySource { get; init; }

    /// <summary>Frontier latency is not measured (no API calls were made).</summary>
    public string FrontierLatencyNote { get; init; } = "Frontier latency is not measured: no API call was made, so the latency mix covers the local side only.";

    /// <summary>The cost estimate, or null when no frontier answer is cached.</summary>
    public CostEstimate? Cost { get; init; }

    /// <summary>Why there is no cost estimate, when there isn't one.</summary>
    public string? CostUnavailable { get; init; }
}

/// <summary>
/// The cascade stage (T026): for each model with a threshold, blends local answers (confidence ≥ τ)
/// with the cached primary-prompt frontier answers (human overrides applied), and prices the result.
/// </summary>
public static class CascadeStage
{
    /// <summary>Simulates one model's cascade from its held-out records.</summary>
    /// <param name="spec">The decision spec.</param>
    /// <param name="threshold">The model's threshold result.</param>
    /// <param name="heldOut">Held-out records from the threshold's source phase.</param>
    /// <param name="frontier">The ingested frontier state.</param>
    /// <param name="latency">Held-out records carrying real call latency (raw or calibrated phase), or null.</param>
    /// <param name="latencySource">Which phase <paramref name="latency"/> came from.</param>
    /// <param name="chars">Primary-prompt character tallies, for the cost estimate.</param>
    /// <param name="gpuMeanWatts">Mean GPU power during the local run.</param>
    /// <param name="secondsPerDecision">Local seconds per decision.</param>
    public static CascadeResult Simulate(
        DecisionSpec spec, ThresholdResult threshold, IReadOnlyList<MeasuredItem> heldOut, FrontierState frontier,
        IReadOnlyList<MeasuredItem>? latency, string? latencySource, CharTally chars, double? gpuMeanWatts, double? secondsPerDecision)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(threshold);
        ArgumentNullException.ThrowIfNull(heldOut);
        ArgumentNullException.ThrowIfNull(frontier);
        ArgumentNullException.ThrowIfNull(chars);
        var q = spec.Question;
        var pv = spec.Frontier.PromptVersion;
        var ok = heldOut.Where(r => r.Error is null && r.ConfidenceMaxp is not null && r.Correct is not null).ToArray();
        int failures = heldOut.Count - ok.Length;
        var localOnly = CountedRate.Of(ok.Length, ok.Count(r => r.Correct == true));
        int frontierN = 0, frontierRight = 0;
        foreach (var r in ok)
        {
            if (frontier.EffectiveAnswer(r.ItemId, pv) is { } a)
            {
                frontierN++;
                frontierRight += q.ClassIndex(a) == q.ClassIndex(r.Gold) ? 1 : 0;
            }
        }

        var frontierOnly = CountedRate.Of(frontierN, frontierRight);
        var okLatency = latency?.Where(r => r.Error is null && r.LatencyMs > 0).Select(r => r.LatencyMs).ToArray() ?? [];
        double? p50 = MetricSet.Percentile(okLatency, 50);
        CostEstimate? Cost(double? share, out string? why)
        {
            why = null;
            if (chars.Answers == 0)
            {
                why = "No frontier answer is cached yet, so frontier tokens can't be estimated.";
                return null;
            }

            return CostModel.Estimate(spec.Pricing, chars, share, gpuMeanWatts, secondsPerDecision);
        }

        if (threshold.Tau is not { } tau)
        {
            var costOnly = Cost(null, out var whyNoCost);
            return new CascadeResult
            {
                Model = threshold.Model,
                Source = threshold.Source,
                NotSimulated = $"No cascade: no threshold met the {Fmt.Pct(threshold.TargetError)} target error on the calibration split.",
                Items = ok.Length,
                ExcludedFailures = failures,
                LocalOnlyAccuracy = localOnly,
                FrontierOnlyAccuracy = frontierOnly,
                LocalLatencyP50Ms = p50,
                LatencySource = latencySource,
                Cost = costOnly,
                CostUnavailable = whyNoCost,
            };
        }

        int kept = 0, escalated = 0, missing = 0, finalN = 0, finalRight = 0, overrides = 0;
        foreach (var r in ok)
        {
            if (r.ConfidenceMaxp!.Value >= tau)
            {
                kept++;
                finalN++;
                finalRight += r.Correct == true ? 1 : 0;
                continue;
            }

            escalated++;
            if (frontier.EffectiveAnswer(r.ItemId, pv) is not { } answer)
            {
                missing++;
                continue;
            }

            overrides += frontier.Overrides.ContainsKey(r.ItemId) ? 1 : 0;
            finalN++;
            finalRight += q.ClassIndex(answer) == q.ClassIndex(r.Gold) ? 1 : 0;
        }

        double? shareEscalated = ok.Length == 0 ? null : (double)escalated / ok.Length;
        var cost = Cost(shareEscalated, out var why);
        return new CascadeResult
        {
            Model = threshold.Model,
            Source = threshold.Source,
            Tau = tau,
            Items = ok.Length,
            ExcludedFailures = failures,
            KeptLocal = kept,
            Escalated = escalated,
            MissingFrontier = missing,
            ShareLocal = ok.Length == 0 ? null : (double)kept / ok.Length,
            ShareEscalated = shareEscalated,
            LocalOnlyAccuracy = localOnly,
            FrontierOnlyAccuracy = frontierOnly,
            BlendedAccuracy = CountedRate.Of(finalN, finalRight),
            OverridesUsed = overrides,
            LocalLatencyP50Ms = p50,
            LatencySource = latencySource,
            Cost = cost,
            CostUnavailable = why,
        };
    }

    /// <summary>Runs the cascade for every model in <c>threshold.json</c> and writes <c>cascade.json</c>.</summary>
    /// <param name="spec">The decision spec.</param>
    /// <param name="manifest">The dataset manifest.</param>
    /// <param name="heldOutItems">The held-out split.</param>
    public static IReadOnlyList<CascadeResult> Run(DecisionSpec spec, DatasetManifest manifest, IReadOnlyList<DatasetItem> heldOutItems)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var thresholds = WorkbenchJson.ReadJson<List<ThresholdResult>>(spec.ThresholdPath);
        var frontier = FrontierStage.LoadState(spec, manifest, heldOutItems);
        var answers = frontier.Accepted.Values.Where(a => a.PromptVersion == spec.Frontier.PromptVersion).ToArray();
        var chars = new CharTally(answers.Length, answers.Sum(a => (long)a.InputChars), answers.Sum(a => (long)a.OutputChars));
        var results = new List<CascadeResult>();
        foreach (var t in thresholds)
        {
            var records = WorkbenchJson.ReadJsonl<MeasuredItem>(spec.RunPath(t.Model, "heldout", t.Source));
            var latencyPhase = File.Exists(spec.RunPath(t.Model, "heldout", Phases.Calibrated)) ? Phases.Calibrated : Phases.Raw;
            var latencyRecords = File.Exists(spec.RunPath(t.Model, "heldout", latencyPhase))
                ? WorkbenchJson.ReadJsonl<MeasuredItem>(spec.RunPath(t.Model, "heldout", latencyPhase)) : null;
            MeasureSummary? summary = File.Exists(spec.RunSummaryPath(t.Model, "heldout", latencyPhase))
                ? WorkbenchJson.ReadJson<MeasureSummary>(spec.RunSummaryPath(t.Model, "heldout", latencyPhase)) : null;
            double? secondsPerDecision = summary is { Items: > 0 } s ? s.DurationSeconds / s.Items : null;
            results.Add(Simulate(spec, t, records, frontier, latencyRecords, latencyRecords is null ? null : latencyPhase,
                chars, summary?.Gpu?.MeanWatts, secondsPerDecision));
        }

        WorkbenchJson.WriteJson(spec.CascadePath, results);
        return results;
    }
}
