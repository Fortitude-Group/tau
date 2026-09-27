using System.Text.Json;
using System.Text.Json.Nodes;
using Tau.Workbench.Spec;

namespace Tau.Workbench.Data;

/// <summary>One prepared item: a stable id, the state text and the gold label.</summary>
/// <param name="Id">The stable item id.</param>
/// <param name="Text">The state sent to the model.</param>
/// <param name="Label">The gold label (an option key, a level index or true/false).</param>
public sealed record DatasetItem(string Id, string Text, string Label);

/// <summary>
/// The committed <c>dataset.manifest.json</c>: the parts the Workbench relies on, plus the whole document
/// for the report's dataset box.
/// </summary>
/// <param name="Document">The whole manifest.</param>
/// <param name="Sha256">The sha256 of the manifest file itself (the dataset revision recorded in calibrators).</param>
/// <param name="SplitSha256">The sha256 recorded for each split file, by split name.</param>
/// <param name="SourceRevision">The pinned source revision, when the manifest records one.</param>
/// <param name="Licence">The dataset licence, when recorded.</param>
/// <param name="Synthetic">Whether the data is synthetic.</param>
/// <param name="Source">The data source, when recorded.</param>
public sealed record DatasetManifest(
    JsonObject Document,
    string Sha256,
    IReadOnlyDictionary<string, string> SplitSha256,
    string? SourceRevision,
    string? Licence,
    bool Synthetic,
    string? Source)
{
    /// <summary>
    /// The short dataset revision used in frontier cache keys: the first 12 characters of the pinned
    /// source revision, or of the held-out split's sha256 when the manifest has no source revision.
    /// </summary>
    public string CacheRevision =>
        SourceRevision is { Length: > 0 } rev ? rev[..Math.Min(12, rev.Length)]
        : SplitSha256.TryGetValue("heldout", out var h) ? h[..Math.Min(12, h.Length)]
        : Sha256[..12];

    /// <summary>
    /// Reads a manifest. The split hashes are read from <c>splits.&lt;name&gt;.sha256</c> (the documented
    /// shape) or, failing that, from a flat <c>sha256.&lt;name&gt;</c> or <c>split_sha256.&lt;name&gt;</c> map.
    /// </summary>
    /// <param name="path">The manifest file.</param>
    /// <exception cref="WorkbenchException">The manifest is missing or unreadable.</exception>
    public static DatasetManifest Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new WorkbenchException(
                $"Dataset manifest {path} is missing. The data has not been prepared: run the sidecar data script for this dataset (sidecar/finetune/tau_sidecar/data_*.py).");
        }

        JsonObject doc;
        try
        {
            doc = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                  ?? throw new WorkbenchException($"Dataset manifest {path} must be a JSON object.");
        }
        catch (JsonException e)
        {
            throw new WorkbenchException($"Dataset manifest {path} does not parse: {e.Message}", e);
        }

        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (doc["splits"] is JsonObject splits)
        {
            foreach (var (name, node) in splits)
            {
                if (node?["sha256"] is JsonValue v && v.TryGetValue<string>(out var s))
                {
                    hashes[name] = s.ToLowerInvariant();
                }
            }
        }

        foreach (var flat in new[] { "split_sha256", "sha256" })
        {
            if (hashes.Count == 0 && doc[flat] is JsonObject map)
            {
                foreach (var (name, node) in map)
                {
                    if (node is JsonValue v && v.TryGetValue<string>(out var s))
                    {
                        hashes[name] = s.ToLowerInvariant();
                    }
                }
            }
        }

        string? revision = Text(doc["revision"]) ?? Text(doc["source"]?["revision"]);
        string? source = Text(doc["source"]) ?? Text(doc["source"]?["repo"]) ?? Text(doc["source"]?["name"]);
        string? licence = Text(doc["licence"]) ?? Text(doc["license"]);
        bool synthetic = doc["synthetic"] is JsonValue sv && sv.TryGetValue<bool>(out var b) && b;
        return new DatasetManifest(doc, WorkbenchJson.Sha256File(path), hashes, revision, licence, synthetic, source);
    }

    private static string? Text(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}

/// <summary>Reads the prepared splits for a decision spec and verifies each against the manifest.</summary>
public static class PreparedDataset
{
    /// <summary>
    /// Loads one prepared split after checking its sha256 against the manifest. Every gold label must be a
    /// class of the spec's question.
    /// </summary>
    /// <param name="spec">The decision spec.</param>
    /// <param name="manifest">The loaded manifest.</param>
    /// <param name="split">calibration, heldout or finetune.</param>
    /// <exception cref="WorkbenchException">
    /// The split file is missing (the data isn't prepared), the manifest has no hash for it, the hash does
    /// not match, an item is malformed, an id repeats, or a gold label is not an allowed answer.
    /// </exception>
    public static IReadOnlyList<DatasetItem> LoadSplit(DecisionSpec spec, DatasetManifest manifest, string split)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(manifest);
        var path = spec.SplitPath(split);
        if (!File.Exists(path))
        {
            throw new WorkbenchException(
                $"Prepared split {path} is missing. The data has not been prepared: run the sidecar data script for '{spec.Data.Dataset}' (sidecar/finetune/tau_sidecar/data_*.py).");
        }

        if (!manifest.SplitSha256.TryGetValue(split, out var expected))
        {
            throw new WorkbenchException($"The dataset manifest {spec.ManifestPath} records no sha256 for the '{split}' split, so the split can't be verified.");
        }

        var actual = WorkbenchJson.Sha256File(path);
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new WorkbenchException(
                $"Prepared split {path} has sha256 {actual}, but the manifest records {expected}. The data on disk is not the data the manifest describes: re-run the sidecar data script.");
        }

        var items = new List<DatasetItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int lineNumber = 0;
        foreach (var line in File.ReadLines(path))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JsonObject obj;
            try
            {
                obj = JsonNode.Parse(line) as JsonObject ?? throw new WorkbenchException($"{path}:{lineNumber} is not a JSON object.");
            }
            catch (JsonException e)
            {
                throw new WorkbenchException($"{path}:{lineNumber} does not parse: {e.Message}", e);
            }

            string id = Scalar(obj["id"]) ?? throw new WorkbenchException($"{path}:{lineNumber} has no 'id'.");
            string text = Scalar(obj[spec.Data.TextField]) ?? throw new WorkbenchException($"{path}:{lineNumber} has no '{spec.Data.TextField}' text field.");
            string label = Scalar(obj[spec.Data.LabelField]) ?? throw new WorkbenchException($"{path}:{lineNumber} has no '{spec.Data.LabelField}' label field.");
            if (!seen.Add(id))
            {
                throw new WorkbenchException($"{path}:{lineNumber} repeats item id '{id}'.");
            }

            if (spec.Question.ClassIndex(label) is null)
            {
                throw new WorkbenchException(
                    $"{path}:{lineNumber} has gold label '{label}', which is not one of the question's answers ({string.Join(", ", spec.Question.Classes.Take(12))}{(spec.Question.Classes.Count > 12 ? ", ..." : "")}).");
            }

            items.Add(new DatasetItem(id, text, label));
        }

        return items;
    }

    /// <summary>A JSON string, number or boolean as text (labels may be written as numbers or booleans).</summary>
    private static string? Scalar(JsonNode? node) => node switch
    {
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        JsonValue v when v.TryGetValue<bool>(out var b) => b ? "true" : "false",
        JsonValue v => v.ToJsonString(),
        _ => null,
    };
}
