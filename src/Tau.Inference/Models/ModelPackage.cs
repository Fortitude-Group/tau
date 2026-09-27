using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tau.Inference.Models;

/// <summary>Model family: decides which sequence builder and post-processor a package uses.</summary>
public enum ModelFamily
{
    /// <summary>Laya (convaiinnovations/laya): encoder + typed decision head, one sequence per question.</summary>
    Laya,

    /// <summary>Von (wfzyx/von): option-marker model with order-invariant masks.</summary>
    Von,
}

/// <summary>Laya token budgets, copied from the checkpoint's <c>rl_agent_config.json</c>.</summary>
/// <param name="MaxLen">Maximum sequence length in tokens.</param>
/// <param name="HeadMaxLen">Token budget for instructions plus options.</param>
/// <param name="MinK">Minimum option slots the graph needs (the head's top-2); pad with masked slots.</param>
public sealed record LayaLimits(int MaxLen, int HeadMaxLen, int MinK);

/// <summary>Von limits.</summary>
/// <param name="MaxPosition">The encoder's maximum position.</param>
/// <param name="SlidingWindow">Local attention half-window used for the sliding mask.</param>
/// <param name="TauMaxTokens">Tau's cap on a packed sequence (a documented deviation; see DECISIONS).</param>
/// <param name="MinK">Minimum option slots the graph is fed.</param>
public sealed record VonLimits(int MaxPosition, int SlidingWindow, int TauMaxTokens, int MinK);

/// <summary>Laya's reference post-processing parameters (temperatures already clamped to [0.5, 5]).</summary>
/// <param name="Temperature">Per question type: choice, score, noul.</param>
/// <param name="TemperatureByOptions">Per <c>type:bucket</c> key, e.g. <c>choice:3-5</c>.</param>
/// <param name="Rounding">Decimal places the reference rounds answers to.</param>
public sealed record LayaPostProcessing(
    IReadOnlyList<double> Temperature, IReadOnlyDictionary<string, double> TemperatureByOptions, int Rounding);

/// <summary>Von's input-conditioned temperature map: T = bias + entropy·H + log_tokens·log10(n)/4 + n_options·K/8, clamped.</summary>
/// <param name="Bias">Constant term.</param>
/// <param name="Entropy">Coefficient on the normalised entropy of the unscaled distribution.</param>
/// <param name="LogTokens">Coefficient on log10(state tokens) / 4.</param>
/// <param name="NOptions">Coefficient on option count / 8.</param>
/// <param name="Lo">Lower clamp.</param>
/// <param name="Hi">Upper clamp.</param>
public sealed record VonCalibrationMap(double Bias, double Entropy, double LogTokens, double NOptions, double Lo, double Hi);

/// <summary>Von's zero-shot noul prior correction: correction = A·bias + B.</summary>
/// <param name="A">Slope on the context-free bias.</param>
/// <param name="B">Intercept.</param>
public sealed record VonNoulPrior(double A, double B);

/// <summary>Von's reference post-processing parameters.</summary>
/// <param name="Temperature">Scalar fallback temperature.</param>
/// <param name="CalibrationMap">Input-conditioned map, or null to use the scalar.</param>
/// <param name="NoulPrior">Fitted noul prior, or null for the 0.7·bias fallback.</param>
/// <param name="IndependentOptions">Order-invariant masks and position ids (true for von-1.2.0).</param>
/// <param name="DigitSplit">Whether digit runs are spaced out before tokenising.</param>
public sealed record VonPostProcessing(
    double Temperature, VonCalibrationMap? CalibrationMap, VonNoulPrior? NoulPrior, bool IndependentOptions, bool DigitSplit);

/// <summary>Raised when a model package is missing, malformed, or its files don't match the recorded hashes.</summary>
public sealed class ModelPackageException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="file">The file at fault.</param>
    /// <param name="problem">What's wrong with it.</param>
    public ModelPackageException(string file, string problem)
        : base($"model package file '{file}': {problem}") => File = file;

    /// <summary>The file at fault.</summary>
    public string File { get; }
}

/// <summary>
/// A loaded <c>tau-model.json</c> package: the exported ONNX model, its tokeniser and the reference
/// post-processing parameters. <see cref="Load"/> verifies every file's sha256 against the manifest and
/// refuses the package on any mismatch (FR-002).
/// </summary>
public sealed class ModelPackage
{
    private ModelPackage(string id, ModelFamily family, string directory, JsonObject manifest)
    {
        Id = id;
        Family = family;
        Directory = directory;
        Manifest = manifest;
    }

    /// <summary>Tau model id, e.g. <c>laya-en</c>.</summary>
    public string Id { get; }

    /// <summary>Model family.</summary>
    public ModelFamily Family { get; }

    /// <summary>Package directory.</summary>
    public string Directory { get; }

    /// <summary>The raw manifest, for reports and diagnostics.</summary>
    public JsonObject Manifest { get; }

    /// <summary>Path of the ONNX graph file.</summary>
    public string OnnxPath => Path.Combine(Directory, Str(Manifest, "onnx", "file"));

    /// <summary>sha256 of the ONNX graph file (the model's identity in reports and calibrators).</summary>
    public string OnnxSha256 => Str(Manifest, "onnx", "sha256");

    /// <summary>Path of <c>tokenizer.json</c>.</summary>
    public string TokenizerPath => Path.Combine(Directory, Str(Manifest, "tokenizer", "file"));

    /// <summary>Path of <c>tokenizer_config.json</c>.</summary>
    public string TokenizerConfigPath => Path.Combine(Directory, Str(Manifest, "tokenizer", "config"));

    /// <summary>Upstream repository the weights came from.</summary>
    public string SourceRepo => Str(Manifest, "source", "repo");

    /// <summary>Pinned upstream commit.</summary>
    public string SourceRevision => Str(Manifest, "source", "revision");

    /// <summary>The reference runtime this package reproduces, e.g. <c>laya==0.3.20</c>.</summary>
    public string ReferencePackage => Str(Manifest, "reference", "package");

    /// <summary>Laya limits (null for Von).</summary>
    public LayaLimits? LayaLimits { get; private init; }

    /// <summary>Laya post-processing (null for Von).</summary>
    public LayaPostProcessing? LayaPost { get; private init; }

    /// <summary>Von limits (null for Laya).</summary>
    public VonLimits? VonLimits { get; private init; }

    /// <summary>Von post-processing (null for Laya).</summary>
    public VonPostProcessing? VonPost { get; private init; }

    /// <summary>Loads and verifies a package directory.</summary>
    /// <param name="directory">Directory containing <c>tau-model.json</c>.</param>
    /// <exception cref="ModelPackageException">Missing file, malformed manifest, or a hash mismatch.</exception>
    public static ModelPackage Load(string directory)
    {
        var manifestPath = Path.Combine(directory, "tau-model.json");
        if (!File.Exists(manifestPath))
            throw new ModelPackageException(manifestPath, "not found; run scripts/export.ps1");

        JsonObject m;
        try
        {
            m = JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject()
                ?? throw new ModelPackageException(manifestPath, "empty manifest");
        }
        catch (JsonException e)
        {
            throw new ModelPackageException(manifestPath, $"malformed JSON: {e.Message}");
        }

        if (m["format"]?.GetValue<string>() != "tau.model" || m["version"]?.GetValue<int>() != 1)
            throw new ModelPackageException(manifestPath, "not a tau.model v1 manifest");

        var family = m["family"]?.GetValue<string>() switch
        {
            "laya" => ModelFamily.Laya,
            "von" => ModelFamily.Von,
            var f => throw new ModelPackageException(manifestPath, $"unknown family '{f}'"),
        };
        var id = m["id"]?.GetValue<string>() ?? throw new ModelPackageException(manifestPath, "missing id");

        // Every file the runtime reads is hash-checked, the multi-GB weights included.
        var checks = new List<(string File, string Hash)>
        {
            (Str(m, "onnx", "file"), Str(m, "onnx", "sha256")),
            (Str(m, "tokenizer", "file"), Str(m, "tokenizer", "sha256")),
            (Str(m, "tokenizer", "config"), Str(m, "tokenizer", "config_sha256")),
        };
        if (m["onnx"]?["data_file"] is not null)
            checks.Add((Str(m, "onnx", "data_file"), Str(m, "onnx", "data_sha256")));

        try
        {
            Parallel.ForEach(checks, c =>
            {
                var path = Path.Combine(directory, c.File);
                if (!File.Exists(path)) throw new ModelPackageException(path, "missing");
                var actual = Sha256(path);
                if (!string.Equals(actual, c.Hash, StringComparison.OrdinalIgnoreCase))
                    throw new ModelPackageException(path, $"sha256 {actual} doesn't match the manifest's {c.Hash}");
            });
        }
        catch (AggregateException ae) when (ae.InnerExceptions.OfType<ModelPackageException>().FirstOrDefault() is { } first)
        {
            throw first;
        }

        var limits = m["limits"]?.AsObject() ?? throw new ModelPackageException(manifestPath, "missing limits");
        var post = m["postProcessing"]?.AsObject() ?? throw new ModelPackageException(manifestPath, "missing postProcessing");

        return family == ModelFamily.Laya
            ? new ModelPackage(id, family, directory, m)
            {
                LayaLimits = new LayaLimits(Int(limits, "max_len"), Int(limits, "head_max_len"), Int(limits, "min_k")),
                LayaPost = new LayaPostProcessing(
                    post["temperature"]!.AsArray().Select(x => x!.GetValue<double>()).ToArray(),
                    post["temperature_by_options"]!.AsObject().ToDictionary(kv => kv.Key, kv => kv.Value!.GetValue<double>()),
                    post["rounding"]?.GetValue<int>() ?? 4),
            }
            : new ModelPackage(id, family, directory, m)
            {
                VonLimits = new VonLimits(Int(limits, "max_position"), Int(limits, "sliding_window"),
                    Int(limits, "tau_max_tokens"), Int(limits, "min_k")),
                VonPost = new VonPostProcessing(
                    post["temperature"]!.GetValue<double>(),
                    post["calibration_map"] is JsonObject cm
                        ? new VonCalibrationMap(D(cm, "bias"), D(cm, "entropy"), D(cm, "log_tokens"), D(cm, "n_options"),
                            cm["lo"]?.GetValue<double>() ?? 0.5, cm["hi"]?.GetValue<double>() ?? 12.0)
                        : null,
                    post["noul_prior"] is JsonObject np ? new VonNoulPrior(D(np, "a"), D(np, "b")) : null,
                    post["independent_options"]?.GetValue<bool>() ?? false,
                    post["digit_split"]?.GetValue<bool>() ?? false),
            };
    }

    /// <summary>Lower-case hex sha256 of a file, streamed.</summary>
    /// <param name="path">File to hash.</param>
    public static string Sha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static string Str(JsonObject m, string section, string key) =>
        m[section]?[key]?.GetValue<string>() ?? throw new ModelPackageException("tau-model.json", $"missing {section}.{key}");

    private static int Int(JsonObject o, string key) =>
        o[key]?.GetValue<int>() ?? throw new ModelPackageException("tau-model.json", $"missing limits.{key}");

    private static double D(JsonObject o, string key) =>
        o[key]?.GetValue<double>() ?? throw new ModelPackageException("tau-model.json", $"missing {key}");
}
