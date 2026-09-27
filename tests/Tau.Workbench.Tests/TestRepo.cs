using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Tau.Workbench.Data;
using Tau.Workbench.Spec;

namespace Tau.Workbench.Tests;

/// <summary>
/// A throwaway repository on disk: a <c>Tau.slnx</c> marker, a decision spec under
/// <c>examples/&lt;name&gt;/</c>, prepared splits under <c>data/&lt;dataset&gt;/</c> and a manifest with their sha256s.
/// </summary>
internal sealed class TestRepo : IDisposable
{
    private TestRepo(string root)
    {
        Root = root;
        SpecDir = Path.Combine(root, "examples", "demo");
        Directory.CreateDirectory(SpecDir);
        File.WriteAllText(Path.Combine(root, "Tau.slnx"), "<Solution />");
    }

    public string Root { get; }

    public string SpecDir { get; }

    public string SpecPath => Path.Combine(SpecDir, "decision.yaml");

    public string DataDir => Path.Combine(Root, "data", "demo");

    public DecisionSpec Spec => DecisionSpec.Load(SpecPath);

    public DatasetManifest Manifest => DatasetManifest.Load(Path.Combine(SpecDir, "dataset.manifest.json"));

    /// <summary>Creates a repo with a spec of the given type and prepared splits.</summary>
    public static TestRepo Create(
        string type = "choice", int calibration = 300, int heldOut = 300, int classes = 4,
        string? pricing = null, bool synthetic = false, string models = "[model-a]", string baselines = "[]",
        double targetError = 0.1, int altSubset = 20)
    {
        var repo = new TestRepo(Path.Combine(Path.GetTempPath(), "tau-wb-" + Guid.NewGuid().ToString("N")[..12]));
        repo.WriteSpec(SpecYaml(type, classes, pricing, models, baselines, targetError, altSubset));
        repo.WriteSplits(type, classes, calibration, heldOut);
        repo.WriteManifest(synthetic);
        return repo;
    }

    public static string Options(string type, int classes) => type switch
    {
        "choice" => "\n" + string.Concat(Enumerable.Range(0, classes).Select(i => $"    {Key(i)}: \"option {Key(i)} description\"\n")),
        "score" => "\n" + string.Concat(Enumerable.Range(0, classes).Select(i => $"    - \"level {i}\"\n")),
        _ => "\n    \"true\": \"it is\"\n    \"false\": \"it is not\"\n",
    };

    public static string Key(int i) => ((char)('a' + (i % 26))).ToString() + (i >= 26 ? (i / 26).ToString(CultureInfo.InvariantCulture) : "");

    public static string DefaultPricing => """
pricing:
  basis_date: "2026-09-27"
  source: "https://example.test/pricing"
  usd_per_mtok:
    frontier-x: [4, 20]
    frontier-x-batch: [2, 10]
    cheap-y: [1, 5]
  headline: frontier-x
  gbp_per_usd: 0.75
  gbp_per_usd_source: "test rate"
  electricity_gbp_per_kwh: 0.25
  electricity_source: "test tariff"
  tokenizer_factor: 1.3
""";

    public static string SpecYaml(string type, int classes, string? pricing, string models, string baselines, double targetError, int altSubset) => $"""
name: demo
title: "Demo decision"
question:
  key: q
  type: {type}
  instructions: "Which option fits this item?"
  options:{Options(type, classes)}
data:
  dataset: demo
  text_field: text
  label_field: label
endpoint: http://localhost:18093
models: {models}
baselines: {baselines}
threshold:
  target_error: {targetError.ToString(CultureInfo.InvariantCulture)}
frontier:
  model: frontier-x
  prompt_version: v1
  alt_prompt_version: v1-alt
  alt_subset: {altSubset}
{pricing ?? DefaultPricing}
""";

    public void WriteSpec(string yaml) => File.WriteAllText(SpecPath, yaml);

    /// <summary>The gold label of item i: classes cycle.</summary>
    public static string LabelOf(string type, int classes, int i) => type switch
    {
        "choice" => Key(i % classes),
        "score" => (i % classes).ToString(CultureInfo.InvariantCulture),
        _ => i % 2 == 0 ? "true" : "false",
    };

    public void WriteSplits(string type, int classes, int calibration, int heldOut)
    {
        Directory.CreateDirectory(DataDir);
        WriteSplit("calibration", Enumerable.Range(0, calibration).Select(i => new DatasetItem($"c{i}", $"calibration item {i}", LabelOf(type, classes, i))));
        WriteSplit("heldout", Enumerable.Range(0, heldOut).Select(i => new DatasetItem($"h{i}", $"held-out item {i}", LabelOf(type, classes, i))));
        WriteSplit("finetune", Enumerable.Range(0, 10).Select(i => new DatasetItem($"f{i}", $"fine-tune item {i}", LabelOf(type, classes, i))));
    }

    public void WriteSplit(string split, IEnumerable<DatasetItem> items)
    {
        var sb = new StringBuilder();
        foreach (var item in items)
        {
            sb.Append(new JsonObject { ["id"] = item.Id, ["text"] = item.Text, ["label"] = item.Label }.ToJsonString()).Append('\n');
        }

        Directory.CreateDirectory(DataDir);
        File.WriteAllText(Path.Combine(DataDir, split + ".jsonl"), sb.ToString());
    }

    public void WriteManifest(bool synthetic = false, string? revision = "0123456789abcdef0123")
    {
        var splits = new JsonObject();
        foreach (var split in DecisionSpec.SplitNames)
        {
            var path = Path.Combine(DataDir, split + ".jsonl");
            splits[split] = new JsonObject
            {
                ["rows"] = File.ReadAllLines(path).Length,
                ["sha256"] = WorkbenchJson.Sha256File(path),
            };
        }

        var manifest = new JsonObject
        {
            ["source"] = "test/source",
            ["licence"] = "CC-BY-4.0",
            ["synthetic"] = synthetic,
            ["seed"] = 42,
            ["splits"] = splits,
        };
        if (revision is not null)
        {
            manifest["revision"] = revision;
        }

        File.WriteAllText(Path.Combine(SpecDir, "dataset.manifest.json"), manifest.ToJsonString());
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
