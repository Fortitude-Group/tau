using System.Text.Json.Nodes;
using Tau.Workbench.Baselines;
using Tau.Workbench.Calibrate;
using Tau.Workbench.Cascade;
using Tau.Workbench.Frontier;
using Tau.Workbench.Measure;
using Tau.Workbench.Threshold;

namespace Tau.Workbench.Report;

/// <summary>Run metadata: everything needed to trace a number back to how it was produced (XIII).</summary>
public sealed record ReportMetadata
{
    /// <summary>When the report was generated (UTC, ISO 8601).</summary>
    public required string GeneratedUtc { get; init; }

    /// <summary>The command that produced the report.</summary>
    public required string Command { get; init; }

    /// <summary>The Workbench version.</summary>
    public required string ToolVersion { get; init; }

    /// <summary>The git state.</summary>
    public required GitInfo Git { get; init; }

    /// <summary>The machine.</summary>
    public required HardwareInfo Hardware { get; init; }

    /// <summary>The endpoint measured, as recorded in the raw runs.</summary>
    public string? Endpoint { get; init; }

    /// <summary>The endpoint's identity (<c>/v1/models</c>), as recorded in the raw runs.</summary>
    public EndpointIdentity? EndpointIdentity { get; init; }

    /// <summary>The model hashes measured, by model id.</summary>
    public required IReadOnlyDictionary<string, string?> ModelHashes { get; init; }

    /// <summary>sha256 of the dataset manifest.</summary>
    public required string DatasetManifestSha256 { get; init; }

    /// <summary>The dataset revision used in frontier cache keys.</summary>
    public required string DatasetRevision { get; init; }

    /// <summary>The frontier prompt versions.</summary>
    public required IReadOnlyList<string> PromptVersions { get; init; }

    /// <summary>The frontier model.</summary>
    public required string FrontierModel { get; init; }

    /// <summary>Accepted frontier answers per prompt version.</summary>
    public required IReadOnlyDictionary<string, int> FrontierCacheCounts { get; init; }

    /// <summary>How confidence is defined.</summary>
    public required string ConfidenceNote { get; init; }
}

/// <summary>The dataset box.</summary>
/// <param name="Name">The dataset name.</param>
/// <param name="Source">Where it came from.</param>
/// <param name="Revision">The pinned source revision.</param>
/// <param name="Licence">Its licence.</param>
/// <param name="Synthetic">Whether it is synthetic.</param>
/// <param name="SplitSizes">Items per split.</param>
/// <param name="Manifest">The whole manifest document.</param>
public sealed record DatasetBox(string Name, string? Source, string? Revision, string? Licence, bool Synthetic, IReadOnlyDictionary<string, int> SplitSizes, JsonObject Manifest);

/// <summary>A confusion view: a matrix for up to 10 classes, otherwise the most frequent confusions.</summary>
/// <param name="Kind">matrix or top.</param>
/// <param name="Labels">Class labels (matrix rows are gold, columns predicted).</param>
/// <param name="Matrix">Counts (matrix kind only).</param>
/// <param name="Top">The most frequent (gold, predicted) confusions (top kind only).</param>
/// <param name="Source">Which measurement it was built from.</param>
/// <param name="N">Items it covers.</param>
public sealed record ConfusionView(string Kind, IReadOnlyList<string> Labels, IReadOnlyList<int[]>? Matrix, IReadOnlyList<Confusion>? Top, string Source, int N);

/// <summary>One off-diagonal confusion.</summary>
/// <param name="Gold">The gold label.</param>
/// <param name="Predicted">What the model said.</param>
/// <param name="Count">How often.</param>
public sealed record Confusion(string Gold, string Predicted, int Count);

/// <summary>Everything measured for one model.</summary>
public sealed record ModelReport
{
    /// <summary>The model id.</summary>
    public required string Model { get; init; }

    /// <summary>The model's ONNX sha256, when known.</summary>
    public string? ModelHash { get; init; }

    /// <summary>Raw held-out summary.</summary>
    public MeasureSummary? RawHeldOut { get; init; }

    /// <summary>Raw calibration-split summary.</summary>
    public MeasureSummary? RawCalibration { get; init; }

    /// <summary>The Runtime's calibrated held-out summary, when the calibrated phase ran.</summary>
    public MeasureSummary? CalibratedHeldOut { get; init; }

    /// <summary>The Workbench's offline calibrated held-out summary.</summary>
    public MeasureSummary? OfflineHeldOut { get; init; }

    /// <summary>What the calibrate stage did.</summary>
    public CalibrationSummary? Calibration { get; init; }

    /// <summary>The calibrated held-out metrics the report headlines: the Runtime's when present, else offline.</summary>
    public MetricSet? BestCalibrated { get; init; }

    /// <summary>Which phase <see cref="BestCalibrated"/> came from.</summary>
    public string? BestCalibratedSource { get; init; }

    /// <summary>Relative held-out ECE reduction, raw to calibrated.</summary>
    public double? EceReduction { get; init; }

    /// <summary>Largest absolute probability difference between the Runtime's calibrated phase and the offline view.</summary>
    public double? RuntimeVsOfflineMaxDiff { get; init; }

    /// <summary>The confusion view.</summary>
    public ConfusionView? Confusion { get; init; }
}

/// <summary>The whole report: <c>report.json</c>, from which <c>report.html</c> is rendered.</summary>
public sealed record ReportDocument
{
    /// <summary>The report title.</summary>
    public required string Title { get; init; }

    /// <summary>The spec name.</summary>
    public required string Name { get; init; }

    /// <summary>The question key.</summary>
    public required string QuestionKey { get; init; }

    /// <summary>choice, score or noul.</summary>
    public required string QuestionType { get; init; }

    /// <summary>The question's instructions.</summary>
    public required string Instructions { get; init; }

    /// <summary>The classes in probability order.</summary>
    public required IReadOnlyList<string> Classes { get; init; }

    /// <summary>The threshold target.</summary>
    public required double TargetError { get; init; }

    /// <summary>The one-paragraph summary.</summary>
    public required string Summary { get; init; }

    /// <summary>The dataset box.</summary>
    public required DatasetBox Dataset { get; init; }

    /// <summary>Per-model results, in spec order.</summary>
    public required IReadOnlyList<ModelReport> Models { get; init; }

    /// <summary>Threshold results.</summary>
    public required IReadOnlyList<ThresholdResult> Thresholds { get; init; }

    /// <summary>Cascade results.</summary>
    public required IReadOnlyList<CascadeResult> Cascades { get; init; }

    /// <summary>Baseline results.</summary>
    public required IReadOnlyList<BaselineResult> Baselines { get; init; }

    /// <summary>The label stage summary, when it has run.</summary>
    public LabelSummary? Frontier { get; init; }

    /// <summary>The misses, stated plainly (XIII).</summary>
    public required IReadOnlyList<string> Misses { get; init; }

    /// <summary>Run metadata.</summary>
    public required ReportMetadata Metadata { get; init; }
}
