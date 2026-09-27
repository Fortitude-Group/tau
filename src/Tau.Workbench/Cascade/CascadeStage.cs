using Tau.Workbench.Data;
using Tau.Workbench.Frontier;
using Tau.Workbench.Measure;
using Tau.Workbench.Reference;
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

    /// <summary>
    /// Set for a hosted endpoint: its label ("hosted endpoint, measured over the network"). Its local share is a call
    /// to it, priced at its own price, and its latency is wall clock with the network included.
    /// </summary>
    public string? Hosted { get; init; }

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

    /// <summary>
    /// What the rates are measured against. Under <see cref="ReferenceKind.Frontier"/> every rate is agreement
    /// with the frontier model's answer, not accuracy.
    /// </summary>
    public ReferenceKind Reference { get; init; } = ReferenceKind.Gold;

    /// <summary>The local model alone on every item.</summary>
    public required CountedRate LocalOnlyAccuracy { get; init; }

    /// <summary>
    /// The frontier model alone, on items that have a frontier answer. Under a frontier reference this is
    /// 100% by construction (the frontier is compared with itself), which <see cref="FrontierOnlyNote"/> says.
    /// </summary>
    public required CountedRate FrontierOnlyAccuracy { get; init; }

    /// <summary>Under a frontier reference, why the frontier-only figure is not a result.</summary>
    public string? FrontierOnlyNote { get; init; }

    /// <summary>
    /// The cascade, on items with a final answer: the share where the served answer (local at or above τ,
    /// frontier below) is right, or under a frontier reference, equals the frontier model's answer.
    /// </summary>
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
    /// <summary>The statement the report makes instead of a frontier-only agreement figure under a frontier reference.</summary>
    public const string FrontierOnlyByConstruction =
        "Frontier only agrees with the frontier model on 100% of items by construction: its answers are the reference, so this is not a result.";

    /// <summary>Simulates one model's cascade from its held-out records.</summary>
    /// <param name="spec">The decision spec.</param>
    /// <param name="threshold">The model's threshold result.</param>
    /// <param name="heldOut">
    /// Held-out records from the threshold's source phase, already scored against the reference
    /// (<see cref="ReferenceLabels.Apply"/>): each record's gold is the reference label.
    /// </param>
    /// <param name="frontier">The ingested frontier state.</param>
    /// <param name="latency">Held-out records carrying real call latency (raw or calibrated phase), or null.</param>
    /// <param name="latencySource">Which phase <paramref name="latency"/> came from.</param>
    /// <param name="chars">Primary-prompt character tallies, for the cost estimate.</param>
    /// <param name="gpuMeanWatts">Mean GPU power during the local run.</param>
    /// <param name="secondsPerDecision">Local seconds per decision.</param>
    /// <param name="hostedLocal">For a hosted endpoint, its per-call price; null for a local model.</param>
    public static CascadeResult Simulate(
        DecisionSpec spec, ThresholdResult threshold, IReadOnlyList<MeasuredItem> heldOut, FrontierState frontier,
        IReadOnlyList<MeasuredItem>? latency, string? latencySource, CharTally chars, double? gpuMeanWatts, double? secondsPerDecision,
        HostedLocalCost? hostedLocal = null)
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
        var reference = spec.Data.Reference;
        string? frontierOnlyNote = reference == ReferenceKind.Frontier ? FrontierOnlyByConstruction : null;
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

            return CostModel.Estimate(spec.Pricing, chars, share, gpuMeanWatts, secondsPerDecision, hostedLocal);
        }

        if (threshold.Tau is not { } tau)
        {
            var costOnly = Cost(null, out var whyNoCost);
            return new CascadeResult
            {
                Model = threshold.Model,
                Source = threshold.Source,
                Hosted = spec.ExternalFor(threshold.Model) is null ? null : ExternalModelSpec.Label,
                NotSimulated = $"No cascade: no threshold met the {Fmt.Pct(threshold.TargetError)} target {(reference == ReferenceKind.Frontier ? "disagreement" : "error")} on the calibration split.",
                Items = ok.Length,
                ExcludedFailures = failures,
                Reference = reference,
                LocalOnlyAccuracy = localOnly,
                FrontierOnlyAccuracy = frontierOnly,
                FrontierOnlyNote = frontierOnlyNote,
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
            Hosted = spec.ExternalFor(threshold.Model) is null ? null : ExternalModelSpec.Label,
            Tau = tau,
            Items = ok.Length,
            ExcludedFailures = failures,
            KeptLocal = kept,
            Escalated = escalated,
            MissingFrontier = missing,
            ShareLocal = ok.Length == 0 ? null : (double)kept / ok.Length,
            ShareEscalated = shareEscalated,
            Reference = reference,
            LocalOnlyAccuracy = localOnly,
            FrontierOnlyAccuracy = frontierOnly,
            FrontierOnlyNote = frontierOnlyNote,
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
    /// <param name="reference">The labels the cascade is scored against; null resolves them from the spec.</param>
    /// <param name="frontier">The ingested frontier state, when the caller already has it; null loads it.</param>
    /// <exception cref="StageBlockedException">A held-out item has no frontier reference label.</exception>
    public static IReadOnlyList<CascadeResult> Run(
        DecisionSpec spec, DatasetManifest manifest, IReadOnlyList<DatasetItem> heldOutItems, ReferenceLabels? reference = null, FrontierState? frontier = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var thresholds = WorkbenchJson.ReadJson<List<ThresholdResult>>(spec.ThresholdPath);
        reference ??= ReferenceLabels.Load(spec, manifest);
        frontier ??= LoadFrontier(spec, manifest, heldOutItems, reference);

        // Cost is per decision served, so only held-out answers are tallied (never the calibration labels).
        var chars = frontier.HeldOutChars(spec.Frontier.PromptVersion);
        var results = new List<CascadeResult>();
        foreach (var t in thresholds)
        {
            var records = reference.Apply(spec.Question, "heldout", WorkbenchJson.ReadJsonl<MeasuredItem>(spec.RunPath(t.Model, "heldout", t.Source)));
            var latencyPhase = File.Exists(spec.RunPath(t.Model, "heldout", Phases.Calibrated)) ? Phases.Calibrated : Phases.Raw;
            var latencyRecords = File.Exists(spec.RunPath(t.Model, "heldout", latencyPhase))
                ? WorkbenchJson.ReadJsonl<MeasuredItem>(spec.RunPath(t.Model, "heldout", latencyPhase)) : null;
            MeasureSummary? summary = File.Exists(spec.RunSummaryPath(t.Model, "heldout", latencyPhase))
                ? WorkbenchJson.ReadJson<MeasureSummary>(spec.RunSummaryPath(t.Model, "heldout", latencyPhase)) : null;
            double? secondsPerDecision = summary is { Items: > 0 } s ? s.DurationSeconds / s.Items : null;
            var hostedLocal = summary?.Hosted is { PricedResponses: > 0 } h
                ? new HostedLocalCost(t.Model, (double)h.InputTokens / h.PricedResponses, (double)h.OutputTokens / h.PricedResponses, h.InputUsdPerMTok, h.OutputUsdPerMTok)
                : null;
            results.Add(Simulate(spec, t, records, frontier, latencyRecords, latencyRecords is null ? null : latencyPhase,
                chars, summary?.Gpu?.MeanWatts, secondsPerDecision, hostedLocal));
        }

        WorkbenchJson.WriteJson(spec.CascadePath, results);
        return results;
    }

    /// <summary>
    /// The frontier state a cascade escalates to: the one the reference already ingested under a frontier
    /// reference, otherwise the cache ingested against the held-out split.
    /// </summary>
    /// <param name="spec">The decision spec.</param>
    /// <param name="manifest">The dataset manifest.</param>
    /// <param name="heldOutItems">The held-out split.</param>
    /// <param name="reference">The resolved reference labels.</param>
    public static FrontierState LoadFrontier(DecisionSpec spec, DatasetManifest manifest, IReadOnlyList<DatasetItem> heldOutItems, ReferenceLabels reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return reference.Frontier ?? FrontierStage.LoadState(spec, manifest, heldOutItems);
    }
}
