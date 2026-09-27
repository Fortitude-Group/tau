using Tau.Workbench.Calibrate;
using Tau.Workbench.Measure;
using Tau.Workbench.Spec;

namespace Tau.Workbench.Report;

/// <summary>What the report says about one hosted endpoint, gathered from its raw runs.</summary>
public sealed record HostedModelReport
{
    /// <summary>The hosted endpoint's id (its name in every table).</summary>
    public required string Id { get; init; }

    /// <summary>How it is labelled beside the local models.</summary>
    public string Label { get; init; } = ExternalModelSpec.Label;

    /// <summary>The endpoint URL.</summary>
    public required string Endpoint { get; init; }

    /// <summary>The request's <c>model</c> field.</summary>
    public required string RequestedModel { get; init; }

    /// <summary>The <c>model</c> strings the responses returned: the endpoint's identity, in place of a model hash.</summary>
    public required IReadOnlyList<string> ModelsReturned { get; init; }

    /// <summary>Input tokens reported over both raw splits.</summary>
    public required long InputTokens { get; init; }

    /// <summary>Output tokens reported over both raw splits.</summary>
    public required long OutputTokens { get; init; }

    /// <summary>Estimated spend over both raw splits, USD (usage × the spec's price; an estimate, not a bill).</summary>
    public required double EstimatedSpendUsd { get; init; }

    /// <summary>The measure run's budget, USD.</summary>
    public required double BudgetUsd { get; init; }

    /// <summary>Input price used, USD per million tokens.</summary>
    public required double InputUsdPerMTok { get; init; }

    /// <summary>Output price used, USD per million tokens.</summary>
    public required double OutputUsdPerMTok { get; init; }

    /// <summary>Items never sent because the spend guard stopped the measure.</summary>
    public int NotSent { get; init; }

    /// <summary>True when the spend guard stopped a split.</summary>
    public bool StoppedAtBudget { get; init; }

    /// <summary>The most decimal places observed in its probabilities.</summary>
    public int? DecimalPlaces { get; init; }

    /// <summary>What the endpoint did with the x-tau request headers.</summary>
    public required string HeadersNote { get; init; }

    /// <summary>What its latency includes.</summary>
    public required string LatencyNote { get; init; }

    /// <summary>Why its calibrated view is the Workbench's offline calibration.</summary>
    public string CalibrationNote { get; init; } = "Calibrated view: the Workbench's offline calibration, because " + OfflineCalibrators.Why;
}

/// <summary>The report's side of hosted endpoints.</summary>
internal static class ReportHosted
{
    /// <summary>The hosted-endpoint facts for a model, or null for a local model or one not measured yet.</summary>
    public static HostedModelReport? For(DecisionSpec spec, string model, MeasureSummary? heldOut, MeasureSummary? calibration)
    {
        if (spec.ExternalFor(model) is not { } ext)
        {
            return null;
        }

        var runs = new[] { calibration?.Hosted, heldOut?.Hosted }.OfType<HostedMeasure>().ToArray();
        if (runs.Length == 0)
        {
            return null;
        }

        return new HostedModelReport
        {
            Id = model,
            Endpoint = runs[^1].Endpoint,
            RequestedModel = runs[^1].RequestedModel,
            ModelsReturned = runs.SelectMany(r => r.ModelsReturned).Distinct().Order(StringComparer.Ordinal).ToArray(),
            InputTokens = runs.Sum(r => r.InputTokens),
            OutputTokens = runs.Sum(r => r.OutputTokens),
            EstimatedSpendUsd = runs.Sum(r => r.EstimatedSpendUsd),
            BudgetUsd = ext.BudgetUsd,
            InputUsdPerMTok = runs[^1].InputUsdPerMTok,
            OutputUsdPerMTok = runs[^1].OutputUsdPerMTok,
            NotSent = runs.Sum(r => r.NotSent),
            StoppedAtBudget = runs.Any(r => r.StoppedAtBudget),
            DecimalPlaces = new[] { calibration?.DecimalPlaces, heldOut?.DecimalPlaces }.Max(),
            HeadersNote = runs[^1].HeadersNote,
            LatencyNote = runs[^1].LatencyNote,
        };
    }

    /// <summary>The hosted endpoints' misses: a measure the spend guard cut short.</summary>
    public static IEnumerable<string> Misses(IReadOnlyList<ModelReport> models) =>
        models.Where(m => m.Hosted is { StoppedAtBudget: true }).Select(m =>
            $"{m.Model}: the spend guard stopped the measure at an estimated {Fmt.Usd(m.Hosted!.EstimatedSpendUsd)} against a {Fmt.Usd(m.Hosted.BudgetUsd)} budget. {Fmt.Int(m.Hosted.NotSent)} item(s) were not sent, so its figures cover only what was measured. Raise budget_usd and run again to finish.");

    /// <summary>The name a model is shown under: a hosted endpoint carries its label.</summary>
    public static string Title(ReportDocument doc, string model) =>
        doc.Models.FirstOrDefault(m => m.Model == model)?.Hosted is { } h ? $"{model} ({h.Label})" : model;
}
