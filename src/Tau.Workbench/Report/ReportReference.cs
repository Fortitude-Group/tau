using Tau.Workbench.Baselines;
using Tau.Workbench.Frontier;
using Tau.Workbench.Measure;
using Tau.Workbench.Reference;
using Tau.Workbench.Spec;

namespace Tau.Workbench.Report;

/// <summary>
/// The report's side of the reference labels: re-scoring every measurement against the spec's reference
/// (so a summary written before the reference changed can never be shown under the wrong name), the
/// secondary view against the dataset's own labels, and the words the report uses for "right".
/// </summary>
internal static class ReportReference
{
    /// <summary>
    /// Reads a measurement summary and recomputes its metrics from the records against the reference. The
    /// summary's other facts (endpoint, latency, GPU) are kept. Null when the phase has not run.
    /// </summary>
    public static MeasureSummary? Rescore(DecisionSpec spec, ReferenceLabels reference, string model, string split, string phase)
    {
        if (!File.Exists(spec.RunSummaryPath(model, split, phase)))
        {
            return null;
        }

        var summary = WorkbenchJson.ReadJson<MeasureSummary>(spec.RunSummaryPath(model, split, phase));
        if (!File.Exists(spec.RunPath(model, split, phase)))
        {
            return summary;
        }

        var records = reference.Apply(spec.Question, split, WorkbenchJson.ReadJsonl<MeasuredItem>(spec.RunPath(model, split, phase)));
        return summary with { Metrics = MeasureStage.Summarise(spec, model, split, phase, records).Metrics, Reference = reference.Kind };
    }

    /// <summary>What a right answer is called: "accuracy" or "agreement with the frontier model".</summary>
    public static string Rate(ReferenceKind reference) => reference == ReferenceKind.Frontier ? "agreement with the frontier model" : "accuracy";

    /// <summary>The reference in one sentence, for the metadata block and the summary.</summary>
    public static string Describe(DecisionSpec spec) => spec.Data.Reference == ReferenceKind.Frontier
        ? $"Scored against the frontier model: every rate is agreement with {spec.Frontier.Model}'s {spec.Frontier.PromptVersion} answers (after any human override), not accuracy against the dataset's labels (data.reference: frontier)."
        : "Scored against the dataset's gold labels (data.reference: gold).";

    /// <summary>Under a frontier reference, the secondary view against the dataset's own labels; null under gold.</summary>
    public static DatasetLabelView? DatasetLabels(
        DecisionSpec spec, ReferenceLabels reference, IReadOnlyList<BaselineResult> baselines, LabelSummary? frontier, bool synthetic)
    {
        if (reference.Kind != ReferenceKind.Frontier || frontier is null)
        {
            return null;
        }

        var q = spec.Question;
        CountedRate? AgainstDataset(string model, string phase)
        {
            if (!File.Exists(spec.RunPath(model, "heldout", phase)))
            {
                return null;
            }

            var ok = WorkbenchJson.ReadJsonl<MeasuredItem>(spec.RunPath(model, "heldout", phase)).Where(r => r.Error is null && r.Answer is not null).ToArray();
            return CountedRate.Of(ok.Length, ok.Count(r => q.ClassIndex(r.Answer) == q.ClassIndex(reference.DatasetLabel(r.ItemId) ?? r.Gold)));
        }

        var rows = new List<DatasetLabelRow>();
        foreach (var model in spec.Models)
        {
            var calibratedPhase = File.Exists(spec.RunPath(model, "heldout", Phases.Calibrated)) ? Phases.Calibrated : Phases.Offline;
            rows.Add(new DatasetLabelRow(model, "tau", AgainstDataset(model, Phases.Raw), AgainstDataset(model, calibratedPhase)));
        }

        rows.AddRange(baselines.Where(b => b.Missing is null).Select(b => new DatasetLabelRow(b.Name, "baseline", b.RawAgainstDatasetLabels, b.CalibratedAgainstDatasetLabels)));
        var noise = frontier.LabelNoise;
        var agreement = CountedRate.Of(noise.N, noise.N - noise.Count);
        rows.Add(new DatasetLabelRow(spec.Frontier.Model, "frontier", agreement, null));

        var labels = reference.Frontier!.Eligible.Select(i => i.Label).ToArray();
        double? majority = labels.Length == 0 ? null : (double)labels.GroupBy(l => l, StringComparer.Ordinal).Max(g => g.Count()) / labels.Length;
        bool arbitrary = agreement.Rate is { } a && majority is { } m && a <= m;
        var traits = new[] { synthetic ? "synthetic" : null, arbitrary ? "close to arbitrary" : null }.OfType<string>().ToArray();
        var caption = (traits.Length == 0
                ? $"The frontier model agrees with the dataset's labels on {Fmt.Pct(agreement.Rate)}."
                : $"The dataset's labels are {string.Join(" and ", traits)}; the frontier model agrees with them on {Fmt.Pct(agreement.Rate)}.")
            + (arbitrary ? $" That is no better than the {Fmt.Pct(majority)} a model would get by always giving the most common label." : "")
            + " This table is a secondary view: every other figure in the report is agreement with the frontier model, because this example asks whether a local model can stand in for the frontier call.";
        return new DatasetLabelView { Caption = caption, FrontierAgreement = agreement, MajorityClassShare = majority, Rows = rows };
    }

    /// <summary>Artefacts fitted or chosen under a different reference than the spec's now names, as misses to re-run.</summary>
    public static IEnumerable<string> StaleArtefacts(
        DecisionSpec spec, IReadOnlyList<ModelReport> models, IReadOnlyList<Threshold.ThresholdResult> thresholds, IReadOnlyList<Cascade.CascadeResult> cascades)
    {
        var want = spec.Data.Reference;
        string Name(ReferenceKind k) => k == ReferenceKind.Frontier ? "the frontier model's answers" : "the dataset's gold labels";
        foreach (var m in models.Where(m => m.Calibration is { } c && c.Reference != want))
        {
            yield return $"{m.Model}'s calibrators were fitted against {Name(m.Calibration!.Reference)}, but this spec scores against {Name(want)}: run 'tau calibrate' again before trusting its calibrated figures.";
        }

        if (thresholds.Any(t => t.Reference != want) || cascades.Any(c => c.Reference != want))
        {
            yield return $"threshold.json or cascade.json was written against {Name(want == ReferenceKind.Gold ? ReferenceKind.Frontier : ReferenceKind.Gold)}: run 'tau threshold' and 'tau cascade' again.";
        }
    }
}
