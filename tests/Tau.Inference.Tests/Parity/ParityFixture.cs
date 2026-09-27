using System.Text.Json.Nodes;
using Tau.Contract;
using Tau.Inference.Engine;
using Tau.Inference.Onnx;

namespace Tau.Inference.Tests.Parity;

/// <summary>
/// The committed reference fixtures (<c>tests/fixtures/parity/&lt;model&gt;/*.jsonl</c>, written by
/// <c>tau_sidecar.parity</c> from laya 0.3.20 / von-sdk 1.2.3 on CPU in FP32) and one shared CPU engine over the
/// real exported models. Parity tolerance, agreed 2026-09-27: |Δlogit| ≤ 2e-3, |Δprob| ≤ 1e-3, same argmax.
/// </summary>
internal static class ParityFixture
{
    public const double TolLogit = 2e-3;
    public const double TolProb = 1e-3;

    public static readonly string[] ModelIds = ["laya-en", "laya-multilingual", "laya-typed-decisions", "von-1.2.0"];

    private static readonly Lazy<OnnxDecisionEngine> LazyEngine = new(() =>
    {
        foreach (var id in ModelIds)
            Assert.True(File.Exists(Path.Combine(RepoRoot.Models, id, "tau-model.json")),
                $"{id} not exported: run scripts/fetch-models.ps1 then scripts/export.ps1");
        return new OnnxDecisionEngine(new EngineSettings
        {
            ModelsDirectory = RepoRoot.Models,
            NativeDirectory = Path.Combine(RepoRoot.Path, "native"),
            CudaDepsDirectory = Path.Combine(RepoRoot.Path, "native", "cuda-deps"),
            Provider = OrtProviderNames.Parse(Environment.GetEnvironmentVariable("TAU_ORT_PROVIDER") ?? "cpu"),
            Preload = false,
        });
    });

    /// <summary>The shared engine (CPU unless TAU_ORT_PROVIDER says otherwise; parity is defined on CPU FP32).</summary>
    public static OnnxDecisionEngine Engine => LazyEngine.Value;

    /// <summary>Reads one fixture file: header plus records keyed by case id.</summary>
    public static (JsonObject Header, Dictionary<string, JsonObject> Cases) Read(string model, string file)
    {
        var path = Path.Combine(RepoRoot.Fixtures, "parity", model, file);
        Assert.True(File.Exists(path), $"{path} missing: run scripts/parity.ps1");
        var lines = File.ReadAllLines(path).Where(l => l.Length > 0).Select(l => JsonNode.Parse(l)!.AsObject()).ToList();
        Assert.True(lines[0]["header"]?.GetValue<bool>() == true, $"{path}: first line must be the header");
        return (lines[0], lines.Skip(1).ToDictionary(r => r["id"]!.GetValue<string>()));
    }

    /// <summary>Builds a contract request from a fixture record's <c>request</c>, pinned to <paramref name="model"/>.</summary>
    public static DecisionRequest Request(JsonObject record, string model)
    {
        var req = record["request"]!.AsObject();
        var body = new JsonObject
        {
            ["model"] = model,
            ["state"] = req["state"]?.DeepClone(),
            ["questions"] = req["questions"]!.DeepClone(),
        };
        Assert.True(ContractParser.TryParse(body.ToJsonString(), out var request, out var problems),
            $"{record["id"]}: fixture request isn't contract-valid: {string.Join("; ", problems.Select(p => $"{p.Path}: {p.Problem}"))}");
        return request!;
    }

    private static readonly object MetricsLock = new();

    /// <summary>
    /// Appends one metrics record to the file named by <c>TAU_PARITY_OUT</c> (set by <c>scripts/parity.ps1</c>),
    /// so the parity report states the measured deviations rather than only pass/fail. A no-op otherwise.
    /// </summary>
    public static void Record(JsonObject metrics)
    {
        if (Environment.GetEnvironmentVariable("TAU_PARITY_OUT") is not { Length: > 0 } path) return;
        lock (MetricsLock) File.AppendAllText(path, metrics.ToJsonString() + Environment.NewLine);
    }

    /// <summary>Whether the header's model hash matches the package the engine loaded (fixtures belong to one export).</summary>
    public static void AssertSameExport(JsonObject header, string model)
    {
        var expected = header["onnx_sha256"]!.GetValue<string>();
        var actual = Engine.Models.Single(m => m.Id == model).OnnxSha256;
        Assert.True(expected == actual, $"{model}: fixtures were generated for ONNX {expected[..12]}…, loaded {actual[..12]}…; rerun scripts/parity.ps1");
    }
}
