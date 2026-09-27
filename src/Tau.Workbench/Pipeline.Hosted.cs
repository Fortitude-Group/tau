using Tau.Workbench.Calibrate;
using Tau.Workbench.Data;
using Tau.Workbench.Measure;
using Tau.Workbench.Reference;
using Tau.Workbench.Spec;

namespace Tau.Workbench;

/// <content>Hosted endpoints (<c>external:</c> in the spec): measured over the network under a spend guard.</content>
public static partial class Pipeline
{
    private static string HostedCalibratedSkipped(ExternalModelSpec ext) =>
        $"measure: {ext.Id} calibrated: skipped: a hosted endpoint can't load calibrators, so the report uses the Workbench's offline calibration.";

    /// <summary>
    /// Measures one hosted endpoint's raw phase on both splits, sharing one spend guard across them. Returns false
    /// when the measure is blocked: no API key, or the spend guard stopped it (what was measured is saved).
    /// </summary>
    private static async Task<bool> MeasureHostedAsync(
        DecisionSpec spec, PipelineOptions options, ExternalModelSpec ext, IReadOnlyDictionary<string, IReadOnlyList<DatasetItem>> splits, CancellationToken ct)
    {
        var o = options.Out;
        if (ApiKeys.Read(ext.ApiKeyEnv, options.ReadEnvironment) is not { } key)
        {
            o.WriteLine($"measure: {ext.Id}: blocked: no API key. Set the {ext.ApiKeyEnv} environment variable (in the process, or at User scope with setx), then run again. Nothing was sent.");
            return false;
        }

        using var endpoint = options.HostedEndpointFactory(ext, key);
        var identity = new EndpointIdentity(false, $"{ExternalModelSpec.Label}: {endpoint.BaseUrl}", [], []);
        var guard = new SpendGuard(ext);
        var measure = options.Measure with
        {
            Hosted = new HostedRun(ext, guard),
            Concurrency = ext.Concurrency,
            StartGpuSampler = () => (null, "Hosted endpoint: no local GPU runs the model, so no GPU power was sampled."),
        };
        o.WriteLine($"measure: {ext.Id}: {ExternalModelSpec.Label} at {endpoint.BaseUrl} (model {ext.Model}), budget {Fmt.Usd(ext.BudgetUsd)}.");
        foreach (var split in MeasuredSplits)
        {
            var s = await MeasureStage.RunAsync(spec, ext.Id, split, Phases.Raw, splits[split], endpoint, identity, measure, ct).ConfigureAwait(false);
            o.WriteLine($"measure: {ext.Id} {split} raw: {s.Measured}/{s.Items} measured, {s.Failures} failed; accuracy {Fmt.Pct(s.Metrics?.Accuracy)}, ECE {Fmt.Num(s.Metrics?.Ece)}; estimated spend {Fmt.Usd(s.Hosted?.EstimatedSpendUsd)}.");
            if (guard.Stopped)
            {
                o.WriteLine($"measure: {ext.Id}: blocked: the spend guard stopped the measure at an estimated {Fmt.Usd(guard.SpentUsd)} of the {Fmt.Usd(ext.BudgetUsd)} budget ({Fmt.Int(guard.InputTokens)} input and {Fmt.Int(guard.OutputTokens)} output tokens). What was measured is saved. Raise budget_usd and run again to finish.");
                return false;
            }
        }

        o.WriteLine($"measure: {ext.Id}: estimated spend {Fmt.Usd(guard.SpentUsd)} of the {Fmt.Usd(ext.BudgetUsd)} budget ({Fmt.Int(guard.InputTokens)} input and {Fmt.Int(guard.OutputTokens)} output tokens, at the spec's prices).");
        return true;
    }

    /// <summary>
    /// <c>tau run</c>'s hosted part: measure each hosted endpoint's raw phase unless current, fit its offline-only
    /// calibrators, and skip the calibrated phase. Returns false when any hosted endpoint is blocked.
    /// </summary>
    private static async Task<bool> RunHostedAsync(
        DecisionSpec spec, PipelineOptions options, DatasetManifest manifest, IReadOnlyDictionary<string, IReadOnlyList<DatasetItem>> splits,
        ReferenceLabels reference, IReadOnlyList<string> splitFiles, IReadOnlyList<string> labelFiles, CancellationToken ct)
    {
        bool ok = true;
        foreach (var ext in spec.External)
        {
            var rawOutputs = Outputs(spec, ext.Id, Phases.Raw);
            if (!options.Force && Staleness.IsCurrent(splitFiles, rawOutputs) && !StoppedAtBudget(spec, ext.Id))
            {
                options.Out.WriteLine($"measure: {ext.Id} raw: skipped (current).");
            }
            else if (!await MeasureHostedAsync(spec, options, ext, splits, ct).ConfigureAwait(false))
            {
                ok = false;
                continue;
            }

            CalibrateIfStale(spec, options, ext.Id, manifest, reference, rawOutputs.Concat(labelFiles));
            options.Out.WriteLine(HostedCalibratedSkipped(ext));
        }

        return ok;
    }

    /// <summary>True when a stored raw measurement of a hosted endpoint was cut short by the spend guard, so it is never current.</summary>
    private static bool StoppedAtBudget(DecisionSpec spec, string model) =>
        MeasuredSplits.Any(split => File.Exists(spec.RunSummaryPath(model, split, Phases.Raw))
            && WorkbenchJson.ReadJson<MeasureSummary>(spec.RunSummaryPath(model, split, Phases.Raw)).Hosted is { StoppedAtBudget: true });
}
