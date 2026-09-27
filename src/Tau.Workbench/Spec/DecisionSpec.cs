using Tau.Calibration;

namespace Tau.Workbench.Spec;

/// <summary>
/// A loaded, validated <c>decision.yaml</c> (data-model.md): the question, the prepared data, the models
/// and endpoint to measure, the threshold target, frontier settings and the price basis. Every artefact
/// path is resolved here, relative to the spec file's directory, so stages never build paths themselves.
/// </summary>
public sealed class DecisionSpec
{
    /// <summary>The names of the three prepared splits.</summary>
    public static readonly IReadOnlyList<string> SplitNames = ["calibration", "heldout", "finetune"];

    internal DecisionSpec(
        string name,
        string title,
        QuestionSpec question,
        DataSpec data,
        Uri? endpoint,
        IReadOnlyList<string> models,
        IReadOnlyList<string> baselines,
        ThresholdSpec threshold,
        FrontierSpec frontier,
        PricingSpec pricing,
        string specPath,
        string repoRoot)
    {
        Name = name;
        Title = title;
        Question = question;
        Data = data;
        Endpoint = endpoint;
        Models = models;
        Baselines = baselines;
        Threshold = threshold;
        Frontier = frontier;
        Pricing = pricing;
        SpecPath = Path.GetFullPath(specPath);
        SpecDirectory = Path.GetDirectoryName(SpecPath)!;
        RepoRoot = repoRoot;
    }

    /// <summary>The example's name (for example <c>banking77</c>).</summary>
    public string Name { get; }

    /// <summary>A human title for the report (defaults to <see cref="Name"/>).</summary>
    public string Title { get; }

    /// <summary>The question.</summary>
    public QuestionSpec Question { get; }

    /// <summary>The prepared data source.</summary>
    public DataSpec Data { get; }

    /// <summary>The endpoint named in the spec, or null when it must be given with <c>--endpoint</c>.</summary>
    public Uri? Endpoint { get; }

    /// <summary>Model ids to measure, in declared order.</summary>
    public IReadOnlyList<string> Models { get; }

    /// <summary>Baseline probability files to include (names under <c>baselines/</c>).</summary>
    public IReadOnlyList<string> Baselines { get; }

    /// <summary>The threshold target.</summary>
    public ThresholdSpec Threshold { get; }

    /// <summary>Frontier labelling settings.</summary>
    public FrontierSpec Frontier { get; }

    /// <summary>The price basis for cost estimates.</summary>
    public PricingSpec Pricing { get; }

    /// <summary>Absolute path of the spec file.</summary>
    public string SpecPath { get; }

    /// <summary>The spec file's directory; every artefact lives under it.</summary>
    public string SpecDirectory { get; }

    /// <summary>The repository root (the nearest ancestor holding <c>Tau.slnx</c> or <c>.git</c>).</summary>
    public string RepoRoot { get; }

    /// <summary>The prepared data directory, <c>&lt;repo&gt;/data/&lt;dataset&gt;</c>.</summary>
    public string DataDirectory => Path.Combine(RepoRoot, "data", Data.Dataset);

    /// <summary>A prepared split file.</summary>
    /// <param name="split">calibration, heldout or finetune.</param>
    public string SplitPath(string split) => Path.Combine(DataDirectory, split + ".jsonl");

    /// <summary>The committed dataset manifest, next to the spec.</summary>
    public string ManifestPath => Path.Combine(SpecDirectory, "dataset.manifest.json");

    /// <summary>The frontier directory.</summary>
    public string FrontierDirectory => Path.Combine(SpecDirectory, "frontier");

    /// <summary>The append-only frontier answer cache.</summary>
    public string CachePath => Path.Combine(FrontierDirectory, "cache.jsonl");

    /// <summary>Human overrides of frontier labels.</summary>
    public string OverridesPath => Path.Combine(FrontierDirectory, "overrides.jsonl");

    /// <summary>The label stage's summary.</summary>
    public string LabelSummaryPath => Path.Combine(FrontierDirectory, "label-summary.json");

    /// <summary>The pending batch directory for one prompt version.</summary>
    /// <param name="promptVersion">The prompt version.</param>
    public string PendingDirectory(string promptVersion) => Path.Combine(FrontierDirectory, "pending", promptVersion);

    /// <summary>The runs directory for one model.</summary>
    /// <param name="model">The model id.</param>
    public string RunsDirectory(string model) => Path.Combine(SpecDirectory, "runs", model);

    /// <summary>A measurement file: <c>runs/&lt;model&gt;/&lt;split&gt;.&lt;phase&gt;.jsonl</c>.</summary>
    /// <param name="model">The model id.</param>
    /// <param name="split">calibration or heldout.</param>
    /// <param name="phase">raw, calibrated or offline.</param>
    public string RunPath(string model, string split, string phase) => Path.Combine(RunsDirectory(model), $"{split}.{phase}.jsonl");

    /// <summary>The summary next to a measurement file.</summary>
    /// <param name="model">The model id.</param>
    /// <param name="split">calibration or heldout.</param>
    /// <param name="phase">raw, calibrated or offline.</param>
    public string RunSummaryPath(string model, string split, string phase) => Path.Combine(RunsDirectory(model), $"{split}.{phase}.summary.json");

    /// <summary>The root of every model's calibrators: what the Runtime's <c>Tau:CalibratorsDirectory</c> points at (it loads subfolders).</summary>
    public string CalibratorsRoot => Path.Combine(SpecDirectory, "calibrators");

    /// <summary>The calibrators directory for one model, under <see cref="CalibratorsRoot"/>.</summary>
    /// <param name="model">The model id.</param>
    public string CalibratorsDirectory(string model) => Path.Combine(SpecDirectory, "calibrators", model);

    /// <summary>The calibrate stage's summary for one model.</summary>
    /// <param name="model">The model id.</param>
    public string CalibrationSummaryPath(string model) => Path.Combine(CalibratorsDirectory(model), "calibration-summary.json");

    /// <summary>A baseline probability file.</summary>
    /// <param name="name">The baseline name.</param>
    public string BaselinePath(string name) => Path.Combine(SpecDirectory, "baselines", name + ".jsonl");

    /// <summary>The threshold stage's output.</summary>
    public string ThresholdPath => Path.Combine(SpecDirectory, "threshold.json");

    /// <summary>The cascade stage's output (cascade and cost).</summary>
    public string CascadePath => Path.Combine(SpecDirectory, "cascade.json");

    /// <summary>The report's JSON twin.</summary>
    public string ReportJsonPath => Path.Combine(SpecDirectory, "report.json");

    /// <summary>The self-contained HTML report.</summary>
    public string ReportHtmlPath => Path.Combine(SpecDirectory, "report.html");

    /// <summary>Loads and validates a decision spec.</summary>
    /// <param name="path">The YAML file.</param>
    /// <exception cref="WorkbenchException">The file is missing.</exception>
    /// <exception cref="SpecValidationException">The file is not a valid decision spec (every problem is listed).</exception>
    public static DecisionSpec Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!File.Exists(path))
        {
            throw new WorkbenchException($"Decision spec {path} does not exist.");
        }

        return DecisionSpecParser.Parse(File.ReadAllText(path), Path.GetFullPath(path));
    }

    /// <summary>
    /// Finds the repository root: the nearest ancestor of <paramref name="startDirectory"/> (inclusive)
    /// holding <c>Tau.slnx</c> or <c>.git</c>, or null.
    /// </summary>
    /// <param name="startDirectory">Where to start.</param>
    public static string? FindRepoRoot(string startDirectory)
    {
        for (var dir = new DirectoryInfo(startDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Tau.slnx"))
                || Directory.Exists(Path.Combine(dir.FullName, ".git"))
                || File.Exists(Path.Combine(dir.FullName, ".git")))
            {
                return dir.FullName;
            }
        }

        return null;
    }
}
