using Tau.Workbench.Baselines;
using Tau.Workbench.Calibrate;
using Tau.Workbench.Cascade;
using Tau.Workbench.Data;
using Tau.Workbench.Frontier;
using Tau.Workbench.Measure;
using Tau.Workbench.Reference;
using Tau.Workbench.Report;
using Tau.Workbench.Spec;
using Tau.Workbench.Threshold;

namespace Tau.Workbench;

/// <summary>Settings shared by every stage entry point.</summary>
public sealed record PipelineOptions
{
    /// <summary>An endpoint that overrides the spec's (<c>--endpoint</c>).</summary>
    public Uri? Endpoint { get; init; }

    /// <summary>Where progress lines go.</summary>
    public TextWriter Out { get; init; } = Console.Out;

    /// <summary>Measurement settings.</summary>
    public MeasureOptions Measure { get; init; } = new();

    /// <summary>Creates the endpoint connection (tests pass a stub-backed one).</summary>
    public Func<Uri, WorkbenchEndpoint> EndpointFactory { get; init; } = uri => new WorkbenchEndpoint(uri);

    /// <summary>
    /// Creates a hosted endpoint's connection from its spec and API key (tests pass a stub-backed one). The default
    /// retries 429 and every 5xx with Tau.Client's backoff.
    /// </summary>
    public Func<ExternalModelSpec, string, WorkbenchEndpoint> HostedEndpointFactory { get; init; } =
        (ext, key) => new WorkbenchEndpoint(ext.Endpoint, retryPolicy: WorkbenchEndpoint.HostedRetryPolicy, apiKey: key);

    /// <summary>Reads an environment variable at a scope (tests pass a fake, so no real key is ever needed).</summary>
    public Func<string, EnvironmentVariableTarget, string?> ReadEnvironment { get; init; } = ApiKeys.Environment;

    /// <summary>The report's command, clock and probes.</summary>
    public ReportContext Report { get; init; } = new() { Command = "tau" };

    /// <summary>The clock for calibrator provenance.</summary>
    public Func<DateTimeOffset> Clock { get; init; } = () => DateTimeOffset.UtcNow;

    /// <summary>
    /// <c>tau run --force</c>: re-measure the raw phase and re-fit even when their outputs look current. File times
    /// can't tell that stored runs were rounded by an endpoint that now honours <c>x-tau-precision: full</c>. The
    /// calibrated phase needs no forcing: re-fitting rewrites the calibrator files, which makes it stale.
    /// </summary>
    public bool Force { get; init; }
}

/// <summary>Exit codes, per the contract: 0 success, 1 error, 2 blocked.</summary>
public static class ExitCodes
{
    /// <summary>Success.</summary>
    public const int Ok = 0;

    /// <summary>An error (bad spec, missing data, unreadable artefact).</summary>
    public const int Error = 1;

    /// <summary>Blocked on something outside the Workbench (pending frontier answers, calibrators not loaded).</summary>
    public const int Blocked = 2;
}

/// <summary>
/// The stage entry points behind the <c>tau</c> commands, and <c>tau run</c>, which runs them all in order,
/// skipping the expensive ones (measure, calibrate) whose outputs are newer than their inputs.
/// </summary>
public static partial class Pipeline
{
    /// <summary>The measured splits.</summary>
    public static readonly IReadOnlyList<string> MeasuredSplits = ["calibration", "heldout"];

    /// <summary><c>tau label</c>: returns 2 while answers are pending, printing the batch files to answer.</summary>
    /// <param name="spec">The decision spec.</param>
    /// <param name="options">Options.</param>
    public static int Label(DecisionSpec spec, PipelineOptions options) => LabelWithSummary(spec, options).Exit;

    private static (int Exit, LabelSummary Summary) LabelWithSummary(DecisionSpec spec, PipelineOptions options)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(options);
        var manifest = DatasetManifest.Load(spec.ManifestPath);
        var calibration = spec.Data.Reference == ReferenceKind.Frontier ? PreparedDataset.LoadSplit(spec, manifest, "calibration") : null;
        var summary = FrontierStage.Run(spec, manifest, PreparedDataset.LoadSplit(spec, manifest, "heldout"), calibration);
        var o = options.Out;
        o.WriteLine($"label: reference is {(spec.Data.Reference == ReferenceKind.Frontier ? $"the frontier model ({spec.Frontier.Model} {spec.Frontier.PromptVersion} answers), so the calibration split is labelled too" : "the dataset's gold labels")}.");
        foreach (var (split, counts) in summary.Splits)
        {
            o.WriteLine($"label: {split}: {string.Join(", ", counts.Cached.Select(kv => $"{kv.Key} {kv.Value} cached, {counts.Pending.GetValueOrDefault(kv.Key)} pending"))}.");
        }

        o.WriteLine($"label: {summary.Rejected.Count} rejected; {summary.Overridden} overridden.");
        if (summary.LabelNoise.Rate is { } noise)
        {
            o.WriteLine($"label: frontier disagrees with gold on {Fmt.Pct(noise)} of {summary.LabelNoise.N}; wordings agree on {Fmt.Pct(summary.PromptAgreement.Rate)} of {summary.PromptAgreement.N}.");
        }

        if (summary.TotalPending == 0)
        {
            o.WriteLine("label: nothing pending.");
            return (ExitCodes.Ok, summary);
        }

        o.WriteLine($"label: {summary.TotalPending} answer(s) pending. Answer these batch files into {Path.GetRelativePath(spec.SpecDirectory, spec.CachePath)}, then run 'tau label' again:");
        foreach (var b in summary.PendingBatches)
        {
            o.WriteLine("  " + Path.Combine(spec.SpecDirectory, b));
        }

        return (ExitCodes.Blocked, summary);
    }

    /// <summary><c>tau measure</c>: measures every model on the calibration and held-out splits in one phase.</summary>
    /// <param name="spec">The decision spec.</param>
    /// <param name="options">Options.</param>
    /// <param name="phase">raw (default) or calibrated.</param>
    /// <param name="ct">Cancellation.</param>
    public static async Task<int> MeasureAsync(DecisionSpec spec, PipelineOptions options, string phase = Phases.Raw, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(options);
        var manifest = DatasetManifest.Load(spec.ManifestPath);
        var splits = MeasuredSplits.ToDictionary(s => s, s => PreparedDataset.LoadSplit(spec, manifest, s));
        options = WithReference(spec, options, manifest, splits);
        using var endpoint = options.EndpointFactory(ResolveEndpoint(spec, options));
        var identity = await endpoint.IdentifyAsync(ct).ConfigureAwait(false);
        options.Out.WriteLine($"measure: endpoint {endpoint.BaseUrl} is {identity.Description}.");
        foreach (var model in spec.Models)
        {
            await MeasureModelAsync(spec, options, model, phase, splits, endpoint, identity, ct).ConfigureAwait(false);
        }

        bool blocked = false;
        foreach (var ext in spec.External)
        {
            if (phase != Phases.Raw)
            {
                options.Out.WriteLine(HostedCalibratedSkipped(ext));
                continue;
            }

            blocked |= !await MeasureHostedAsync(spec, options, ext, splits, ct).ConfigureAwait(false);
        }

        return blocked ? ExitCodes.Blocked : ExitCodes.Ok;
    }

    /// <summary><c>tau calibrate</c>: fits and writes calibrators for every model with raw measurements.</summary>
    /// <param name="spec">The decision spec.</param>
    /// <param name="options">Options.</param>
    public static int Calibrate(DecisionSpec spec, PipelineOptions options)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(options);
        var manifest = DatasetManifest.Load(spec.ManifestPath);
        var reference = ReferenceLabels.Load(spec, manifest);
        foreach (var model in spec.AllModels)
        {
            if (spec.ExternalFor(model) is not null && !File.Exists(spec.RunSummaryPath(model, "calibration", Phases.Raw)))
            {
                options.Out.WriteLine($"calibrate: {model}: skipped: the hosted endpoint has no raw calibration measurement yet.");
                continue;
            }

            var s = CalibrateStage.Run(spec, model, manifest, options.Clock, reference);
            options.Out.WriteLine(s.OfflineOnly
                ? $"calibrate: {model}: {string.Join("; ", s.Calibrators.Select(c => $"{c.Scope} -> {c.Chosen}"))}; offline held-out ECE {Fmt.Num(s.RawHeldOut?.Ece)} -> {Fmt.Num(s.OfflineHeldOut?.Ece)}. Offline only: {OfflineCalibrators.Why}"
                : $"calibrate: {model}: {string.Join("; ", s.Calibrators.Select(c => $"{c.Scope} -> {c.Chosen}"))}; offline held-out ECE {Fmt.Num(s.RawHeldOut?.Ece)} -> {Fmt.Num(s.OfflineHeldOut?.Ece)}. Point the Runtime's Tau:CalibratorsDirectory at {spec.CalibratorsRoot} for the calibrated phase.");
        }

        return ExitCodes.Ok;
    }

    /// <summary><c>tau threshold</c>.</summary>
    /// <param name="spec">The decision spec.</param>
    /// <param name="options">Options.</param>
    public static int Threshold(DecisionSpec spec, PipelineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        foreach (var t in ThresholdStage.Run(spec))
        {
            options.Out.WriteLine($"threshold: {t.Model} ({t.Source}): {t.Note}");
        }

        return ExitCodes.Ok;
    }

    /// <summary>
    /// <c>tau cascade</c>: the Tau models' cascades (written to <c>cascade.json</c>) and the baselines' (which the
    /// report recomputes from their probability files). Returns 2 when escalated items have no cached frontier answer.
    /// </summary>
    /// <param name="spec">The decision spec.</param>
    /// <param name="options">Options.</param>
    public static int Cascade(DecisionSpec spec, PipelineOptions options)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(options);
        var manifest = DatasetManifest.Load(spec.ManifestPath);
        var heldOut = PreparedDataset.LoadSplit(spec, manifest, "heldout");
        var reference = ReferenceLabels.Load(spec, manifest);
        var frontier = CascadeStage.LoadFrontier(spec, manifest, heldOut, reference);
        var results = CascadeStage.Run(spec, manifest, heldOut, reference, frontier)
            .Select(c => (Name: c.Model, Result: c))
            .Concat(BaselineStage.Run(spec, reference, frontier).Where(b => b.Cascade is not null).Select(b => (Name: $"{b.Name} (baseline)", Result: b.Cascade!)))
            .ToArray();
        foreach (var (name, c) in results)
        {
            options.Out.WriteLine(c.Tau is null
                ? $"cascade: {name}: {c.NotSimulated}"
                : $"cascade: {name}: τ {Fmt.Num(c.Tau, "0.00")} keeps {Fmt.Pct(c.ShareLocal)} local; cascade {Fmt.Pct(c.BlendedAccuracy?.Rate)} vs frontier only {Fmt.Pct(c.FrontierOnlyAccuracy.Rate)}; {c.MissingFrontier} escalated item(s) without a frontier answer.");
        }

        if (results.Any(r => r.Result.MissingFrontier > 0))
        {
            options.Out.WriteLine("cascade: blocked on frontier answers: run 'tau label' and answer the pending batches.");
            return ExitCodes.Blocked;
        }

        return ExitCodes.Ok;
    }

    /// <summary><c>tau report</c>.</summary>
    /// <param name="spec">The decision spec.</param>
    /// <param name="options">Options.</param>
    public static int Report(DecisionSpec spec, PipelineOptions options)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(options);
        var manifest = DatasetManifest.Load(spec.ManifestPath);
        var doc = ReportBuilder.Run(spec, manifest, options.Report);
        options.Out.WriteLine($"report: wrote {spec.ReportHtmlPath} and report.json ({doc.Misses.Count} miss(es) listed).");
        return ExitCodes.Ok;
    }

    /// <summary>
    /// <c>tau run</c>: label, measure (raw), calibrate, the calibrated phase when the Runtime has the
    /// calibrators loaded, threshold, cascade and report. Measure and calibrate are skipped when their
    /// outputs are newer than their inputs, unless <see cref="PipelineOptions.Force"/> is set. Returns 2 when frontier answers are pending (after still
    /// producing everything that doesn't need them), 0 otherwise.
    /// </summary>
    /// <param name="spec">The decision spec.</param>
    /// <param name="options">Options.</param>
    /// <param name="ct">Cancellation.</param>
    public static async Task<int> RunAsync(DecisionSpec spec, PipelineOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(options);
        var o = options.Out;
        var (labelExit, labels) = LabelWithSummary(spec, options);
        if (spec.Data.Reference == ReferenceKind.Frontier && labels.Pending.GetValueOrDefault(spec.Frontier.PromptVersion) > 0)
        {
            o.WriteLine($"run: blocked: this spec scores against the frontier model's answers (data.reference: frontier), and {labels.Pending[spec.Frontier.PromptVersion]} {spec.Frontier.PromptVersion} answer(s) are still pending. Every later stage needs them; answer the batches above and run again.");
            return ExitCodes.Blocked;
        }

        var manifest = DatasetManifest.Load(spec.ManifestPath);
        var splits = MeasuredSplits.ToDictionary(s => s, s => PreparedDataset.LoadSplit(spec, manifest, s));
        options = WithReference(spec, options, manifest, splits);
        var reference = options.Measure.Reference!;
        var splitFiles = MeasuredSplits.Select(spec.SplitPath).Append(spec.SpecPath).ToArray();

        // Under a frontier reference the labels are an input to calibration: a changed answer or override re-fits.
        var labelFiles = spec.Data.Reference == ReferenceKind.Frontier ? new[] { spec.CachePath, spec.OverridesPath } : [];

        WorkbenchEndpoint? endpoint = null;
        EndpointIdentity? identity = null;
        try
        {
            foreach (var model in spec.Models)
            {
                var rawOutputs = Outputs(spec, model, Phases.Raw);
                if (!options.Force && Staleness.IsCurrent(splitFiles, rawOutputs))
                {
                    o.WriteLine($"measure: {model} raw: skipped (current).");
                }
                else
                {
                    endpoint ??= options.EndpointFactory(ResolveEndpoint(spec, options));
                    identity ??= await endpoint.IdentifyAsync(ct).ConfigureAwait(false);
                    await MeasureModelAsync(spec, options, model, Phases.Raw, splits, endpoint, identity, ct).ConfigureAwait(false);
                }

                CalibrateIfStale(spec, options, model, manifest, reference, rawOutputs.Concat(labelFiles));

                var calibratorFiles = Directory.Exists(spec.CalibratorsDirectory(model))
                    ? Directory.EnumerateFiles(spec.CalibratorsDirectory(model), "*.calibrator.json").ToArray() : [];
                if (Staleness.IsCurrent(splitFiles.Concat(calibratorFiles), Outputs(spec, model, Phases.Calibrated)))
                {
                    o.WriteLine($"measure: {model} calibrated: skipped (current).");
                    continue;
                }

                endpoint ??= options.EndpointFactory(ResolveEndpoint(spec, options));
                identity ??= await endpoint.IdentifyAsync(ct).ConfigureAwait(false);
                if (!identity.IsTau || !await CalibratorsLoadedAsync(spec, model, splits["calibration"], endpoint, ct).ConfigureAwait(false))
                {
                    o.WriteLine($"measure: {model} calibrated: skipped: the endpoint has not loaded calibrators for it. The report uses the Workbench's offline calibration. To measure the Runtime's own calibrated output, restart it with Tau:CalibratorsDirectory={spec.CalibratorsRoot} and run 'tau measure <spec> --phase calibrated'.");
                    continue;
                }

                await MeasureModelAsync(spec, options, model, Phases.Calibrated, splits, endpoint, identity, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            endpoint?.Dispose();
        }

        bool hostedBlocked = !await RunHostedAsync(spec, options, manifest, splits, reference, splitFiles, labelFiles, ct).ConfigureAwait(false);
        Threshold(spec, options);
        int cascadeExit = Cascade(spec, options);
        Report(spec, options);
        return labelExit == ExitCodes.Blocked || cascadeExit == ExitCodes.Blocked || hostedBlocked ? ExitCodes.Blocked : ExitCodes.Ok;
    }

    /// <summary>The measurement files (records and summaries, both splits) for one model and phase.</summary>
    /// <param name="spec">The decision spec.</param>
    /// <param name="model">The model id.</param>
    /// <param name="phase">The phase.</param>
    public static string[] Outputs(DecisionSpec spec, string model, string phase) =>
        MeasuredSplits.SelectMany(s => new[] { spec.RunPath(model, s, phase), spec.RunSummaryPath(model, s, phase) }).ToArray();

    private static void CalibrateIfStale(
        DecisionSpec spec, PipelineOptions options, string model, DatasetManifest manifest, ReferenceLabels reference, IEnumerable<string> inputs)
    {
        var calibrateOutputs = new[] { spec.CalibrationSummaryPath(model) }.Concat(Outputs(spec, model, Phases.Offline)).ToArray();
        if (!options.Force && Staleness.IsCurrent(inputs, calibrateOutputs))
        {
            options.Out.WriteLine($"calibrate: {model}: skipped (current).");
            return;
        }

        var s = CalibrateStage.Run(spec, model, manifest, options.Clock, reference);
        options.Out.WriteLine(s.OfflineOnly
            ? $"calibrate: {model}: offline-only calibrators written to {spec.CalibratorsDirectory(model)}."
            : $"calibrate: {model}: calibrators written to {spec.CalibratorsDirectory(model)}.");
    }

    private static async Task MeasureModelAsync(
        DecisionSpec spec, PipelineOptions options, string model, string phase, IReadOnlyDictionary<string, IReadOnlyList<DatasetItem>> splits,
        WorkbenchEndpoint endpoint, EndpointIdentity identity, CancellationToken ct)
    {
        foreach (var split in MeasuredSplits)
        {
            var s = await MeasureStage.RunAsync(spec, model, split, phase, splits[split], endpoint, identity, options.Measure, ct).ConfigureAwait(false);
            options.Out.WriteLine($"measure: {model} {split} {phase}: {s.Measured}/{s.Items} measured, {s.Failures} failed; accuracy {Fmt.Pct(s.Metrics?.Accuracy)}, ECE {Fmt.Num(s.Metrics?.Ece)}.");
        }
    }

    /// <summary>Sends one calibration item without <c>x-tau-raw</c> and checks whether a calibrator was applied.</summary>
    private static async Task<bool> CalibratorsLoadedAsync(DecisionSpec spec, string model, IReadOnlyList<DatasetItem> items, WorkbenchEndpoint endpoint, CancellationToken ct)
    {
        if (items.Count == 0)
        {
            return false;
        }

        var call = await endpoint.DecideAsync(MeasureStage.BuildRequest(spec.Question, model, items[0].Text), raw: false, ct).ConfigureAwait(false);
        return call.Response is not null && call.Calibrators is { } c && !string.Equals(c, "none", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolves the reference labels once and checks every item of the measured splits has one, before any
    /// endpoint call, so a missing frontier label stops the run up front instead of after the measuring.
    /// </summary>
    private static PipelineOptions WithReference(
        DecisionSpec spec, PipelineOptions options, DatasetManifest manifest, IReadOnlyDictionary<string, IReadOnlyList<DatasetItem>> splits)
    {
        var reference = ReferenceLabels.Load(spec, manifest);
        foreach (var (split, items) in splits)
        {
            reference.Require(split, items);
        }

        return options with { Measure = options.Measure with { Reference = reference } };
    }

    private static Uri ResolveEndpoint(DecisionSpec spec, PipelineOptions options) =>
        options.Endpoint ?? spec.Endpoint
        ?? throw new WorkbenchException($"No endpoint to measure: set 'endpoint' in {spec.SpecPath} or pass --endpoint URL.");
}

/// <summary>Decides whether a stage's outputs are current.</summary>
public static class Staleness
{
    /// <summary>
    /// True when every output exists and the oldest output is newer than the newest input that exists.
    /// </summary>
    /// <param name="inputs">Input files (missing ones are ignored).</param>
    /// <param name="outputs">Output files.</param>
    public static bool IsCurrent(IEnumerable<string> inputs, IEnumerable<string> outputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(outputs);
        var outs = outputs.ToArray();
        if (outs.Length == 0 || outs.Any(p => !File.Exists(p)))
        {
            return false;
        }

        var newestInput = inputs.Where(File.Exists).Select(File.GetLastWriteTimeUtc).DefaultIfEmpty(DateTime.MinValue).Max();
        return outs.Select(File.GetLastWriteTimeUtc).Min() > newestInput;
    }
}
