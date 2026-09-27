using System.Globalization;
using System.Text.Json.Nodes;
using Tau.Calibration;
using Tau.Workbench.Baselines;
using Tau.Workbench.Calibrate;
using Tau.Workbench.Cascade;
using Tau.Workbench.Data;
using Tau.Workbench.Frontier;
using Tau.Workbench.Measure;
using Tau.Workbench.Spec;
using Tau.Workbench.Threshold;

namespace Tau.Workbench.Report;

/// <summary>What the report needs from outside the artefacts: the command, the clock, and host probes.</summary>
public sealed record ReportContext
{
    /// <summary>The command line that produced the report.</summary>
    public required string Command { get; init; }

    /// <summary>The clock.</summary>
    public Func<DateTimeOffset> Clock { get; init; } = () => DateTimeOffset.UtcNow;

    /// <summary>Hardware probe.</summary>
    public Func<HardwareInfo> Hardware { get; init; } = HostProbe.Hardware;

    /// <summary>Git probe (repo root, spec directory).</summary>
    public Func<string, string, GitInfo> Git { get; init; } = HostProbe.Git;
}

/// <summary>
/// The report stage (T028): gathers every artefact on disk into one <see cref="ReportDocument"/>, writes it
/// as <c>report.json</c>, and renders <c>report.html</c> from that same document, so every number in the
/// HTML is in the JSON. Stages that haven't run yet show as missing rather than failing the report.
/// </summary>
public static class ReportBuilder
{
    /// <summary>The ECE reduction SC-002 asks of the out-of-the-box model.</summary>
    public const double TargetEceReduction = 0.5;

    /// <summary>Builds the document, writes both files, and returns the document.</summary>
    /// <param name="spec">The decision spec.</param>
    /// <param name="manifest">The dataset manifest.</param>
    /// <param name="context">Command, clock and probes.</param>
    public static ReportDocument Run(DecisionSpec spec, DatasetManifest manifest, ReportContext context)
    {
        var doc = Build(spec, manifest, context);
        WorkbenchJson.WriteJson(spec.ReportJsonPath, doc);
        WorkbenchJson.WriteAllTextAtomic(spec.ReportHtmlPath, HtmlReport.Render(doc));
        return doc;
    }

    /// <summary>Builds the document from the artefacts on disk without writing anything.</summary>
    /// <param name="spec">The decision spec.</param>
    /// <param name="manifest">The dataset manifest.</param>
    /// <param name="context">Command, clock and probes.</param>
    public static ReportDocument Build(DecisionSpec spec, DatasetManifest manifest, ReportContext context)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(context);
        var models = spec.Models.Select(m => ModelReportFor(spec, m)).ToArray();
        var thresholds = File.Exists(spec.ThresholdPath) ? WorkbenchJson.ReadJson<List<ThresholdResult>>(spec.ThresholdPath) : [];
        var cascades = File.Exists(spec.CascadePath) ? WorkbenchJson.ReadJson<List<CascadeResult>>(spec.CascadePath) : [];
        var baselines = BaselineStage.Run(spec);
        var frontier = File.Exists(spec.LabelSummaryPath) ? WorkbenchJson.ReadJson<LabelSummary>(spec.LabelSummaryPath) : null;
        var firstRaw = models.Select(m => m.RawHeldOut ?? m.RawCalibration).FirstOrDefault(s => s is not null);

        var dataset = new DatasetBox(
            spec.Data.Dataset, manifest.Source, manifest.SourceRevision, manifest.Licence, manifest.Synthetic,
            SplitSizes(spec, manifest), manifest.Document);
        var metadata = new ReportMetadata
        {
            GeneratedUtc = context.Clock().UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            Command = context.Command,
            ToolVersion = CalibrationFitter.ToolVersion,
            Git = context.Git(spec.RepoRoot, spec.SpecDirectory),
            Hardware = context.Hardware(),
            Endpoint = firstRaw?.Endpoint,
            EndpointIdentity = firstRaw?.EndpointIdentity,
            ModelHashes = models.ToDictionary(m => m.Model, m => m.ModelHash),
            DatasetManifestSha256 = manifest.Sha256,
            DatasetRevision = manifest.CacheRevision,
            PromptVersions = [spec.Frontier.PromptVersion, spec.Frontier.AltPromptVersion],
            FrontierModel = spec.Frontier.Model,
            FrontierCacheCounts = frontier?.Cached ?? new Dictionary<string, int>(),
            ConfidenceNote = MetricSet.ConfidenceNote,
        };

        return new ReportDocument
        {
            Title = spec.Title,
            Name = spec.Name,
            QuestionKey = spec.Question.Key,
            QuestionType = spec.Question.Type.ToWireString(),
            Instructions = spec.Question.Instructions,
            Classes = spec.Question.Classes,
            TargetError = spec.Threshold.TargetError,
            Summary = Summary(spec, dataset, models, cascades, frontier),
            Dataset = dataset,
            Models = models,
            Thresholds = thresholds,
            Cascades = cascades,
            Baselines = baselines,
            Frontier = frontier,
            Misses = Misses(spec, models, thresholds, cascades, baselines, frontier),
            Metadata = metadata,
        };
    }

    private static ModelReport ModelReportFor(DecisionSpec spec, string model)
    {
        MeasureSummary? Summary(string split, string phase) =>
            File.Exists(spec.RunSummaryPath(model, split, phase)) ? WorkbenchJson.ReadJson<MeasureSummary>(spec.RunSummaryPath(model, split, phase)) : null;
        var rawHeld = Summary("heldout", Phases.Raw);
        var calHeld = Summary("heldout", Phases.Calibrated);
        var offHeld = Summary("heldout", Phases.Offline);
        var calibration = File.Exists(spec.CalibrationSummaryPath(model)) ? WorkbenchJson.ReadJson<CalibrationSummary>(spec.CalibrationSummaryPath(model)) : null;
        var (best, bestSource) = calHeld?.Metrics is { } c ? (c, Phases.Calibrated) : offHeld?.Metrics is { } o ? (o, Phases.Offline) : ((MetricSet?)null, (string?)null);
        double? reduction = rawHeld?.Metrics is { Ece: > 0 } raw && best is not null ? (raw.Ece - best.Ece) / raw.Ece : null;

        double? maxDiff = null;
        if (calHeld is not null && offHeld is not null)
        {
            var offline = WorkbenchJson.ReadJsonl<MeasuredItem>(spec.RunPath(model, "heldout", Phases.Offline)).ToDictionary(r => r.ItemId);
            foreach (var r in WorkbenchJson.ReadJsonl<MeasuredItem>(spec.RunPath(model, "heldout", Phases.Calibrated)))
            {
                if (r.Vector(spec.Question) is { } a && offline.TryGetValue(r.ItemId, out var o2) && o2.Vector(spec.Question) is { } b)
                {
                    maxDiff = Math.Max(maxDiff ?? 0, a.Zip(b, (x, y) => Math.Abs(x - y)).Max());
                }
            }
        }

        var confusionPhase = new[] { Phases.Calibrated, Phases.Offline, Phases.Raw }.FirstOrDefault(p => File.Exists(spec.RunPath(model, "heldout", p)));
        return new ModelReport
        {
            Model = model,
            ModelHash = rawHeld?.ModelHash ?? calibration?.ModelHash,
            RawHeldOut = rawHeld,
            RawCalibration = Summary("calibration", Phases.Raw),
            CalibratedHeldOut = calHeld,
            OfflineHeldOut = offHeld,
            Calibration = calibration,
            BestCalibrated = best,
            BestCalibratedSource = bestSource,
            EceReduction = reduction,
            RuntimeVsOfflineMaxDiff = maxDiff,
            Confusion = confusionPhase is null ? null
                : BuildConfusion(spec.Question, WorkbenchJson.ReadJsonl<MeasuredItem>(spec.RunPath(model, "heldout", confusionPhase)), confusionPhase),
        };
    }

    /// <summary>A matrix for up to 10 classes, otherwise the 15 most frequent confusions.</summary>
    /// <param name="question">The question.</param>
    /// <param name="records">Held-out records.</param>
    /// <param name="source">Which phase the records came from.</param>
    public static ConfusionView BuildConfusion(QuestionSpec question, IReadOnlyList<MeasuredItem> records, string source)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(records);
        var classes = question.Classes;
        var ok = records.Where(r => r.Error is null && r.Answer is not null).ToArray();
        if (classes.Count <= 10)
        {
            var m = classes.Select(_ => new int[classes.Count]).ToArray();
            foreach (var r in ok)
            {
                m[question.ClassIndex(r.Gold)!.Value][question.ClassIndex(r.Answer)!.Value]++;
            }

            return new ConfusionView("matrix", classes, m, null, source, ok.Length);
        }

        var top = ok.Where(r => r.Correct == false)
            .GroupBy(r => (r.Gold, r.Answer!))
            .Select(g => new Confusion(g.Key.Gold, g.Key.Item2, g.Count()))
            .OrderByDescending(c => c.Count).ThenBy(c => c.Gold, StringComparer.Ordinal).ThenBy(c => c.Predicted, StringComparer.Ordinal)
            .Take(15).ToArray();
        return new ConfusionView("top", classes, null, top, source, ok.Length);
    }

    private static IReadOnlyDictionary<string, int> SplitSizes(DecisionSpec spec, DatasetManifest manifest)
    {
        var sizes = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var split in DecisionSpec.SplitNames)
        {
            var node = manifest.Document["splits"]?[split];
            foreach (var field in new[] { "rows", "count", "size", "n", "items" })
            {
                if (node?[field] is JsonValue v && v.TryGetValue<int>(out var n))
                {
                    sizes[split] = n;
                    break;
                }
            }

            if (!sizes.ContainsKey(split) && File.Exists(spec.SplitPath(split)))
            {
                sizes[split] = File.ReadLines(spec.SplitPath(split)).Count(l => !string.IsNullOrWhiteSpace(l));
            }
        }

        return sizes;
    }

    private static string Summary(DecisionSpec spec, DatasetBox dataset, IReadOnlyList<ModelReport> models, IReadOnlyList<CascadeResult> cascades, LabelSummary? frontier)
    {
        var parts = new List<string>
        {
            $"This report measures how far {models.Count} model(s) can be trusted on \"{spec.Question.Instructions.Trim()}\", using {(dataset.Synthetic ? "synthetic " : "")}{dataset.Name} data ({(dataset.SplitSizes.TryGetValue("heldout", out var h) ? Fmt.Int(h) + " held-out items" : "held-out size unknown")}).",
        };
        var lead = models.FirstOrDefault(m => m.RawHeldOut?.Metrics is not null);
        if (lead?.RawHeldOut?.Metrics is { } raw)
        {
            parts.Add(lead.BestCalibrated is { } cal
                ? $"Out of the box, {lead.Model} is {Fmt.Pct(raw.Accuracy)} accurate with a calibration error (ECE) of {Fmt.Num(raw.Ece)}; after calibration its ECE is {Fmt.Num(cal.Ece)}, {Fmt.Pct(lead.EceReduction)} lower{(lead.EceReduction >= TargetEceReduction ? ", which meets the 50% goal" : ", short of the 50% goal")}."
                : $"Out of the box, {lead.Model} is {Fmt.Pct(raw.Accuracy)} accurate with a calibration error (ECE) of {Fmt.Num(raw.Ece)}; it has not been calibrated yet.");
        }

        var c = cascades.FirstOrDefault(x => x.Model == lead?.Model && x.Tau is not null) ?? cascades.FirstOrDefault(x => x.Tau is not null);
        if (c is not null)
        {
            var headline = c.Cost?.Rows.FirstOrDefault(r => r.Kind == "headline");
            parts.Add($"At a {Fmt.Pct(spec.Threshold.TargetError)} target error, {c.Model} keeps {Fmt.Pct(c.ShareLocal)} of decisions local; the cascade is {Fmt.Pct(c.BlendedAccuracy?.Rate)} accurate against {Fmt.Pct(c.FrontierOnlyAccuracy.Rate)} for {spec.Frontier.Model} alone"
                + (headline?.CascadeGbpPerMillion is { } cg && headline.FrontierOnlyGbpPerMillion is { } fg
                    ? $", at an estimated {Fmt.Gbp(cg)} per million decisions against {Fmt.Gbp(fg)} (list prices, {spec.Pricing.BasisDate})."
                    : ".")
                + (c.MissingFrontier > 0 ? $" {c.MissingFrontier} escalated item(s) have no frontier answer yet and are excluded." : ""));
        }
        else if (cascades.Count > 0)
        {
            parts.Add($"No model reaches the {Fmt.Pct(spec.Threshold.TargetError)} target error, so no cascade is simulated.");
        }

        if (frontier?.LabelNoise.Rate is { } noise)
        {
            parts.Add($"The frontier model disagrees with the gold labels on {Fmt.Pct(noise)} of {Fmt.Int(frontier.LabelNoise.N)} items, which bounds how much any accuracy figure here can be trusted.");
        }

        return string.Join(" ", parts);
    }

    private static IReadOnlyList<string> Misses(
        DecisionSpec spec, IReadOnlyList<ModelReport> models, IReadOnlyList<ThresholdResult> thresholds,
        IReadOnlyList<CascadeResult> cascades, IReadOnlyList<BaselineResult> baselines, LabelSummary? frontier)
    {
        var misses = new List<string>();
        foreach (var m in models.Where(m => m.EceReduction is not null && m.EceReduction < TargetEceReduction))
        {
            misses.Add($"Calibration cut {m.Model}'s held-out ECE by only {Fmt.Pct(m.EceReduction)} (from {Fmt.Num(m.RawHeldOut!.Metrics!.Ece)} to {Fmt.Num(m.BestCalibrated!.Ece)}), short of the 50% goal.");
        }

        var calibrated = models.Where(m => m.EceReduction is not null).ToArray();
        if (calibrated.Length > 1)
        {
            var least = calibrated.MinBy(m => m.EceReduction!.Value)!;
            misses.Add($"Calibration helped {least.Model} least: {Fmt.Pct(least.EceReduction)} lower held-out ECE.");
        }

        foreach (var b in baselines.Where(b => b.Raw is not null))
        {
            foreach (var m in models.Where(m => m.RawHeldOut?.Metrics is not null))
            {
                var tauAcc = m.RawHeldOut!.Metrics!.Accuracy;
                if (b.Raw!.Accuracy > tauAcc)
                {
                    misses.Add($"Baseline {b.Name} beats {m.Model} on held-out accuracy: {Fmt.Pct(b.Raw.Accuracy)} against {Fmt.Pct(tauAcc)}.");
                }

                if (b.Calibrated is { } bc && m.BestCalibrated is { } mc && bc.Ece < mc.Ece)
                {
                    misses.Add($"After calibration, {b.Name} is also better calibrated than {m.Model}: ECE {Fmt.Num(bc.Ece)} against {Fmt.Num(mc.Ece)}.");
                }
            }
        }

        foreach (var t in thresholds.Where(t => !t.Reachable))
        {
            misses.Add($"{t.Model}: {t.Note}");
        }

        foreach (var c in cascades.Where(c => c.MissingFrontier > 0))
        {
            misses.Add($"{c.Model}: {c.MissingFrontier} escalated item(s) have no cached frontier answer and are left out of the blended accuracy.");
        }

        foreach (var m in models)
        {
            foreach (var s in new[] { m.RawCalibration, m.RawHeldOut, m.CalibratedHeldOut }.OfType<MeasureSummary>())
            {
                if (s.Failures > 0)
                {
                    misses.Add($"{m.Model}: {s.Failures} of {s.Items} {s.Split} items failed in the {s.Phase} phase and are excluded from its metrics.");
                }

                if (s.Truncated > 0)
                {
                    misses.Add($"{m.Model}: the Runtime truncated {s.Truncated} {s.Split} item(s) in the {s.Phase} phase to fit the model; they are measured as truncated.");
                }
            }

            if (m.Calibration?.ClassesWithoutCalibrationExamples is { Count: > 0 } missing)
            {
                misses.Add($"{m.Model}: {missing.Count} class(es) had no calibration example and share the question-type calibrator: {string.Join(", ", missing.Take(10))}{(missing.Count > 10 ? ", ..." : "")}.");
            }
        }

        if (frontier?.LabelNoise.Rate is { } noise)
        {
            misses.Add($"Label noise: the frontier model disagrees with gold on {Fmt.Pct(noise)} ({Fmt.Int(frontier.LabelNoise.Count)} of {Fmt.Int(frontier.LabelNoise.N)}).");
        }

        if (frontier is { TotalPending: > 0 })
        {
            misses.Add($"{Fmt.Int(frontier.TotalPending)} frontier answer(s) are still pending, so frontier figures cover only the answered items.");
        }

        return misses;
    }
}
