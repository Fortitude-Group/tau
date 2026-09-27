using System.Globalization;
using Tau.Calibration;
using Tau.Workbench.Data;
using Tau.Workbench.Measure;
using Tau.Workbench.Reference;
using Tau.Workbench.Spec;

namespace Tau.Workbench.Calibrate;

/// <summary>One calibrator written by the calibrate stage.</summary>
/// <param name="File">The file, relative to the spec directory.</param>
/// <param name="Scope">"question type" or the option-count bucket it covers.</param>
/// <param name="N">Calibration items it was fitted on.</param>
/// <param name="Chosen">The method written to the file.</param>
/// <param name="Temperature">The temperature fit and its calibration-split scores.</param>
/// <param name="Isotonic">The isotonic fit and its scores, or null when not attempted.</param>
/// <param name="IsotonicSkipped">Why isotonic was not attempted, when it wasn't.</param>
/// <param name="CalibrationEceBefore">Calibration-split ECE before calibration.</param>
/// <param name="CalibrationLogLossBefore">Calibration-split log loss before calibration.</param>
public sealed record WrittenCalibrator(
    string File, string Scope, int N, string Chosen, MethodFit Temperature, MethodFit? Isotonic, string? IsotonicSkipped,
    double CalibrationEceBefore, double CalibrationLogLossBefore);

/// <summary>What the calibrate stage did for one model: <c>calibrators/&lt;model&gt;/calibration-summary.json</c>.</summary>
public sealed record CalibrationSummary
{
    /// <summary>The model id.</summary>
    public required string Model { get; init; }

    /// <summary>The labels the calibrators were fitted against (older summaries without the field were fitted on gold).</summary>
    public ReferenceKind Reference { get; init; } = ReferenceKind.Gold;

    /// <summary>
    /// The ONNX sha256 every calibrator was bound to. For an offline-only fit (a hosted endpoint, which has no model
    /// hash) it is the sha256 of the raw calibration run the calibrators were fitted on.
    /// </summary>
    public required string ModelHash { get; init; }

    /// <summary>
    /// True for a hosted endpoint: the calibrators are written as offline-only files (<see cref="OfflineCalibrators"/>)
    /// that the Runtime never loads, and the offline view is the calibrated view.
    /// </summary>
    public bool OfflineOnly { get; init; }

    /// <summary>The dataset manifest's sha256 (the calibrators' dataset revision).</summary>
    public required string DatasetRevision { get; init; }

    /// <summary>
    /// The precision of the raw calibration probabilities the calibrators were fitted on (see
    /// <see cref="Precisions"/>); null for a fit on a run recorded before the Workbench asked for full precision.
    /// </summary>
    public string? RawPrecision { get; init; }

    /// <summary>The most decimal places observed in the raw calibration probabilities, or null when unknown.</summary>
    public int? RawDecimalPlaces { get; init; }

    /// <summary>The calibrators written.</summary>
    public required IReadOnlyList<WrittenCalibrator> Calibrators { get; init; }

    /// <summary>Calibration-split items excluded because their raw measurement failed.</summary>
    public required int ExcludedFailures { get; init; }

    /// <summary>Classes with no calibration example (they share the question-type calibrator).</summary>
    public required IReadOnlyList<string> ClassesWithoutCalibrationExamples { get; init; }

    /// <summary>Held-out metrics with the calibrator applied offline by the Workbench.</summary>
    public MetricSet? OfflineHeldOut { get; init; }

    /// <summary>Held-out raw metrics, for comparison.</summary>
    public MetricSet? RawHeldOut { get; init; }

    /// <summary>Plain-English notes on choices the stage made.</summary>
    public required IReadOnlyList<string> Notes { get; init; }
}

/// <summary>
/// The calibrate stage (T024): fits calibrators on the calibration split's raw measurements only, writes
/// them in the R1 calibrator format for the Runtime to load, and applies them offline to both splits'
/// raw probabilities (<c>&lt;split&gt;.offline.jsonl</c>) so the calibrated numbers exist before the Runtime is
/// restarted. The offline view must equal the Runtime's calibrated phase; the report compares the two.
/// </summary>
public static class CalibrateStage
{
    /// <summary>Runs the stage for one model.</summary>
    /// <param name="spec">The decision spec.</param>
    /// <param name="model">The model id.</param>
    /// <param name="manifest">The dataset manifest.</param>
    /// <param name="clock">Clock for the provenance date (defaults to UTC now).</param>
    /// <param name="reference">The labels to fit against; null resolves them from the spec.</param>
    /// <exception cref="WorkbenchException">
    /// The raw calibration measurement is missing, or has no model hash because the endpoint wasn't Tau
    /// (calibrators are bound to a model hash), or no calibration item succeeded.
    /// </exception>
    /// <exception cref="StageBlockedException">A calibration or held-out item has no frontier reference label.</exception>
    public static CalibrationSummary Run(DecisionSpec spec, string model, DatasetManifest manifest, Func<DateTimeOffset>? clock = null, ReferenceLabels? reference = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(manifest);
        reference ??= ReferenceLabels.Load(spec, manifest);
        var q = spec.Question;
        var rawSummary = WorkbenchJson.ReadJson<MeasureSummary>(spec.RunSummaryPath(model, "calibration", Phases.Raw));
        var hosted = spec.ExternalFor(model);
        string modelHash;
        if (hosted is not null)
        {
            // No model hash exists for a hosted endpoint: bind the offline-only files to the run they were fitted on.
            modelHash = WorkbenchJson.Sha256File(spec.RunPath(model, "calibration", Phases.Raw));
        }
        else if (rawSummary.ModelHash is not { Length: 64 } h)
        {
            throw new WorkbenchException(
                $"The raw calibration measurement for '{model}' has no model hash: the endpoint was {rawSummary.EndpointIdentity?.Description ?? "not identified"}. Calibrators are bound to a model's ONNX sha256, so they can only be fitted against a Tau Runtime that lists the model in GET /v1/models.");
        }
        else
        {
            modelHash = h;
        }

        var records = reference.Apply(q, "calibration", WorkbenchJson.ReadJsonl<MeasuredItem>(spec.RunPath(model, "calibration", Phases.Raw)));
        var usable = records.Where(r => r.Error is null && r.Vector(q) is not null).ToArray();
        if (usable.Length == 0)
        {
            throw new WorkbenchException($"No calibration item for '{model}' has a usable raw answer, so nothing can be fitted.");
        }

        var probs = usable.Select(r => r.Vector(q)!).ToArray();
        var gold = usable.Select(r => q.ClassIndex(r.Gold)!.Value).ToArray();
        string date = (clock ?? (() => DateTimeOffset.UtcNow))().UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var dir = spec.CalibratorsDirectory(model);
        Directory.CreateDirectory(dir);
        foreach (var stale in Directory.EnumerateFiles(dir, "*.calibrator.json").Concat(Directory.EnumerateFiles(dir, "*" + OfflineCalibrators.Suffix)))
        {
            File.Delete(stale);
        }

        var notes = new List<string>();
        if (hosted is not null)
        {
            notes.Add("Offline only: " + OfflineCalibrators.Why);
        }

        int? decimalPlaces = rawSummary.DecimalPlaces ?? Precisions.MaxDecimalPlaces(q, records);
        if (Precisions.CalibratorNote(rawSummary.Precision, decimalPlaces) is { } precisionNote)
        {
            notes.Add(precisionNote);
        }

        var written = new List<WrittenCalibrator>();
        var files = new List<CalibratorFile>();
        var typeWire = q.Type.ToWireString();
        string FileName(string? bucketName) => hosted is not null ? OfflineCalibrators.FileName(model, typeWire, bucketName)
            : bucketName is null ? $"{model}.{typeWire}.calibrator.json" : $"{model}.{typeWire}.{bucketName}.calibrator.json";
        var context = new FitContext(spec, dir, model, modelHash, manifest, date, probs, gold, hosted);
        written.Add(Write(context, null, FileName(null), "question type", files));

        var bucket = OptionBucketExtensions.ForOptionCount(q.Classes.Count);
        if (probs.Length >= CalibrationFitter.MinIsotonicItems)
        {
            var bucketName = bucket.ToWireString().Replace("+", "plus", StringComparison.Ordinal);
            written.Add(Write(context, bucket, FileName(bucketName), $"{bucket.ToWireString()} options", files));
            notes.Add($"A spec asks one question, so every item has {q.Classes.Count} options and the {bucket.ToWireString()} bucket calibrator is fitted on the same items as the question-type one. The Runtime prefers the bucket file for {q.Classes.Count}-option requests; the type-level file covers other option counts.");
        }
        else
        {
            notes.Add($"No option-count bucket calibrator: the bucket has {probs.Length} calibration items, below the {CalibrationFitter.MinIsotonicItems} needed.");
        }

        var missingClasses = q.Classes.Where((c, i) => !gold.Contains(i)).ToArray();
        if (missingClasses.Length > 0)
        {
            notes.Add($"{missingClasses.Length} class(es) have no calibration example; the calibrators are fitted per question type, so those classes use the same calibrator as every other class.");
        }

        // A local model's calibrators are read back through the Runtime's own loader; a hosted endpoint's never are.
        var applied = hosted is not null ? files[^1]
            : CalibratorSet.LoadDirectory(dir).Find(model, q.Type, q.Classes.Count)
              ?? throw new WorkbenchException($"The calibrators just written to {dir} don't resolve for '{model}'. This is a Workbench bug.");
        MetricSet? offlineHeld = null, rawHeld = null;
        foreach (var split in new[] { "calibration", "heldout" })
        {
            var rawPath = spec.RunPath(model, split, Phases.Raw);
            if (!File.Exists(rawPath))
            {
                notes.Add($"No raw {split} measurement, so no offline calibrated {split} view was written.");
                continue;
            }

            var summary = ApplyOffline(spec, model, split, applied, reference);
            if (split == "heldout")
            {
                offlineHeld = summary.Metrics;
                rawHeld = MeasureStage.Summarise(spec, model, split, Phases.Raw, reference.Apply(q, split, WorkbenchJson.ReadJsonl<MeasuredItem>(rawPath))).Metrics;
            }
        }

        var result = new CalibrationSummary
        {
            Model = model,
            Reference = reference.Kind,
            ModelHash = modelHash,
            OfflineOnly = hosted is not null,
            RawPrecision = rawSummary.Precision,
            RawDecimalPlaces = decimalPlaces,
            DatasetRevision = manifest.Sha256,
            Calibrators = written,
            ExcludedFailures = records.Count - usable.Length,
            ClassesWithoutCalibrationExamples = missingClasses,
            OfflineHeldOut = offlineHeld,
            RawHeldOut = rawHeld,
            Notes = notes,
        };
        WorkbenchJson.WriteJson(spec.CalibrationSummaryPath(model), result);
        return result;
    }

    /// <summary>
    /// Applies a calibrator to a split's raw records and writes <c>&lt;split&gt;.offline.jsonl</c> and its summary.
    /// Failed raw items stay failed.
    /// </summary>
    /// <param name="spec">The decision spec.</param>
    /// <param name="model">The model id.</param>
    /// <param name="split">calibration or heldout.</param>
    /// <param name="calibrator">The calibrator to apply.</param>
    /// <param name="reference">The labels the summary scores against; null resolves them from the spec.</param>
    public static MeasureSummary ApplyOffline(DecisionSpec spec, string model, string split, CalibratorFile calibrator, ReferenceLabels? reference = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(calibrator);
        reference ??= ReferenceLabels.Load(spec);
        var q = spec.Question;
        var cal = new Calibrator(calibrator);
        var raw = WorkbenchJson.ReadJsonl<MeasuredItem>(spec.RunPath(model, split, Phases.Raw));
        var offline = raw.Select(r => r.Vector(q) is { } v
            ? r.WithVector(q, cal.ApplyToProbabilities(v)) with { LatencyMs = 0, ModelMs = null, Calibrators = null, ModelHash = null }
            : r with { LatencyMs = 0, ModelMs = null, Calibrators = null, ModelHash = null }).ToArray();
        var scored = MeasureStage.Summarise(spec, model, split, Phases.Offline, reference.Apply(q, split, offline));
        var rawSummaryPath = spec.RunSummaryPath(model, split, Phases.Raw);
        var rawSummary = File.Exists(rawSummaryPath) ? WorkbenchJson.ReadJson<MeasureSummary>(rawSummaryPath) : null;
        var summary = scored with
        {
            StartedUtc = "",
            Reference = reference.Kind,
            ModelHash = spec.ExternalFor(model) is null ? calibrator.ModelHash : null,
            Precision = rawSummary?.Precision,
            DecimalPlaces = rawSummary?.DecimalPlaces,
            ExclusionNote = "Offline: the Workbench applied the fitted calibrator to the raw probabilities; no endpoint was called. " + scored.ExclusionNote,
        };
        WorkbenchJson.WriteJsonl(spec.RunPath(model, split, Phases.Offline), offline);
        WorkbenchJson.WriteJson(spec.RunSummaryPath(model, split, Phases.Offline), summary);
        return summary;
    }

    private sealed record FitContext(
        DecisionSpec Spec, string Dir, string Model, string ModelHash, DatasetManifest Manifest, string Date,
        double[][] Probs, int[] Gold, ExternalModelSpec? Hosted);

    private static WrittenCalibrator Write(FitContext c, OptionBucket? bucket, string fileName, string scope, List<CalibratorFile> files)
    {
        var (spec, probs) = (c.Spec, c.Probs);
        var fit = CalibrationFitter.Fit(probs, c.Gold, c.Model, c.ModelHash, spec.Question.Type, bucket, spec.Name, c.Manifest.Sha256, c.Date);
        var path = Path.Combine(c.Dir, fileName);
        if (c.Hosted is { } hosted)
        {
            OfflineCalibrators.Write(path, fit.File, hosted, $"sha256 of {Path.GetRelativePath(spec.SpecDirectory, spec.RunPath(c.Model, "calibration", Phases.Raw)).Replace('\\', '/')}, the run it was fitted on: a hosted endpoint has no model hash");
        }
        else
        {
            fit.File.Write(path);
        }

        files.Add(fit.File);
        return new WrittenCalibrator(
            Path.GetRelativePath(spec.SpecDirectory, path).Replace('\\', '/'), scope, probs.Length,
            fit.File.Method.ToWireString(), fit.Temperature, fit.Isotonic, fit.IsotonicSkipped, fit.EceBefore, fit.LogLossBefore);
    }
}
