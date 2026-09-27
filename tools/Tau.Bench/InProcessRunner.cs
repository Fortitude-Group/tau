using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.ML.OnnxRuntime;
using Tau.Contract;
using Tau.Inference.Engine;
using Tau.Inference.Onnx;

namespace Tau.Bench;

/// <summary>Raised when a run can't produce an honestly labelled number; the bench then writes no report.</summary>
internal sealed class BenchRefusedException(string message) : Exception(message);

/// <summary>Times the engine in this process: the model forward pass and the whole <c>DecideAsync</c> call.</summary>
internal static class InProcessRunner
{
    public static BenchReport Run(BenchOptions o, Workload workload, Func<LoadSample> sampleLoad)
    {
        var sessionDefaults = new OrtSessionSettings();
        var settings = new EngineSettings
        {
            ModelsDirectory = o.ModelsDir,
            NativeDirectory = o.NativeDir,
            CudaDepsDirectory = o.CudaDepsDir,
            Provider = o.Provider,
            AllowCpuFallback = false, // a GPU that fails to start must fail the run, never quietly become a CPU number
            Models = o.Models,
            Preload = true,
            CalibratorsDirectory = null,
            Session = sessionDefaults with { Warn = m => Console.Error.WriteLine("warning: " + m) },
        };

        Console.WriteLine($"loading models on {o.Provider.Flavour()} from {o.ModelsDir} ...");
        OnnxDecisionEngine engine;
        try
        {
            engine = new OnnxDecisionEngine(settings);
        }
        catch (OrtProviderUnavailableException e)
        {
            throw new BenchRefusedException($"the {o.Provider.Flavour()} provider could not start, so there is nothing honest to report: {e.Message}");
        }

        using (engine)
        {
            var ids = engine.Models.Select(m => m.Id).ToArray();
            var check = VerifyProvider(o.Provider, engine, ids);
            var ortNative = OrtEnv.Instance().GetVersionString();
            var models = ids.Select(id => ModelMeta.FromPackage(engine.Model(id).Package)).ToArray();

            var forward = new List<Cell>();
            var endToEnd = new List<Cell>();
            var perCell = new Dictionary<(string, int), (List<Summary> Fwd, List<Summary> E2e, int Rows, int Tokens)>();
            var requests = ids.SelectMany(id => o.Questions.Select(q => (id, q)))
                .ToDictionary(k => k, k => workload.Request(k.id, k.q));

            var loadBefore = sampleLoad();
            for (var rep = 1; rep <= o.Repeat; rep++)
            {
                foreach (var id in ids)
                foreach (var q in o.Questions)
                {
                    var request = requests[(id, q)];
                    Console.Write($"  repeat {rep}/{o.Repeat}  {id,-22} q={q,-2} ");
                    var (fwd, e2e, rows, tokens) = Measure(engine, request, q, o.Warmup, o.Iterations);
                    var key = (id, q);
                    if (!perCell.TryGetValue(key, out var acc)) perCell[key] = acc = ([], [], rows, tokens);
                    acc.Fwd.Add(Stats.Summarise(fwd, q));
                    acc.E2e.Add(Stats.Summarise(e2e, q));
                    Console.WriteLine($"forward p50 {acc.Fwd[^1].P50:0.00} ms  engine p50 {acc.E2e[^1].P50:0.00} ms");
                }
            }
            var loadAfter = sampleLoad();

            foreach (var id in ids)
            foreach (var q in o.Questions)
            {
                var c = perCell[(id, q)];
                forward.Add(new Cell(id, q, c.Rows, c.Tokens, c.Fwd, Stats.RelativeDiffPercent(c.Fwd[0].P50, c.Fwd.ElementAtOrDefault(1)?.P50)));
                endToEnd.Add(new Cell(id, q, c.Rows, c.Tokens, c.E2e, Stats.RelativeDiffPercent(c.E2e[0].P50, c.E2e.ElementAtOrDefault(1)?.P50)));
            }

            var measurements = new[]
            {
                new Measurement("model_forward", "Model forward pass",
                    "Time inside ONNX Runtime's Run call for the request's single batched forward pass (DecisionDiagnostics.ModelMilliseconds). "
                    + "On a GPU it includes copying the inputs to the device and the logits back. "
                    + "Excludes tokenising, building the rows and post-processing.", forward),
                new Measurement("engine_end_to_end", "Engine end to end",
                    "Stopwatch around OnnxDecisionEngine.DecideAsync on an already-parsed request: routing, tokenising, building the rows, "
                    + "the forward pass (including host-device copies) and the reference post-processing. No HTTP, no JSON.", endToEnd),
            };

            return Assemble(o, workload, "in-process", check, ortNative, models, measurements, loadBefore, loadAfter, sessionDefaults);
        }
    }

    private static (List<double> Fwd, List<double> E2e, int Rows, int Tokens) Measure(
        OnnxDecisionEngine engine, DecisionRequest request, int q, int warmup, int iterations)
    {
        var options = new DecisionOptions(Raw: false);
        var first = engine.DecideAsync(request, options, CancellationToken.None).GetAwaiter().GetResult();
        if (first.Response.Answers.Count != q)
            throw new BenchRefusedException($"{request.Model}: expected {q} answers, got {first.Response.Answers.Count}");
        if (first.Diagnostics.Truncated)
            throw new BenchRefusedException($"{request.Model}: the workload state was truncated; the benchmark must not measure a truncated input");
        for (var i = 1; i < warmup; i++) engine.DecideAsync(request, options, CancellationToken.None).GetAwaiter().GetResult();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var fwd = new List<double>(iterations);
        var e2e = new List<double>(iterations);
        for (var i = 0; i < iterations; i++)
        {
            var t0 = Stopwatch.GetTimestamp();
            var r = engine.DecideAsync(request, options, CancellationToken.None).GetAwaiter().GetResult();
            e2e.Add(Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
            fwd.Add(r.Diagnostics.ModelMilliseconds);
        }
        return (fwd, e2e, first.Diagnostics.BatchRows, first.Diagnostics.InputTokens);
    }

    /// <summary>
    /// Refuses the run unless every session is on the requested provider with no fallback, and (for CUDA) this process
    /// holds a CUDA context according to nvidia-smi.
    /// </summary>
    private static ProviderCheck VerifyProvider(OrtProvider requested, OnnxDecisionEngine engine, IReadOnlyList<string> ids)
    {
        var evidence = new List<string> { "engine built with AllowCpuFallback=false, so a provider that can't start throws instead of falling back" };
        foreach (var id in ids)
        {
            var s = engine.Model(id).Session;
            if (s.Provider != requested || s.FallbackReason is not null)
                throw new BenchRefusedException($"{id}: asked for {requested.Flavour()} but the session runs on {s.Provider.Flavour()}"
                    + (s.FallbackReason is null ? "" : $" ({s.FallbackReason})") + ". Refusing to publish a mislabelled number.");
        }
        evidence.Add($"every session reports provider {requested.OrtName()} with no fallback reason ({string.Join(", ", ids)})");
        if (engine.ActualProvider != requested)
            throw new BenchRefusedException($"engine.ActualProvider is {engine.ActualProvider}, not {requested}. Refusing to publish.");
        evidence.Add($"engine.ActualProvider = {engine.ActualProvider}");

        if (requested == OrtProvider.Cuda)
        {
            switch (HostInfo.ProcessOnGpu())
            {
                case true:
                    evidence.Add($"nvidia-smi lists this process (pid {Environment.ProcessId}) as a compute client");
                    break;
                case false:
                    throw new BenchRefusedException(
                        $"CUDA sessions were created but nvidia-smi doesn't list this process (pid {Environment.ProcessId}) as a GPU compute client. Refusing to publish.");
                default:
                    evidence.Add("nvidia-smi could not list compute processes, so GPU residency was not independently confirmed");
                    break;
            }
        }
        return new ProviderCheck(requested.Flavour(), engine.ActualProvider!.Value.Flavour(), true, evidence);
    }

    internal static BenchReport Assemble(BenchOptions o, Workload workload, string kind, ProviderCheck check, string? ortNative,
        IReadOnlyList<ModelInfo> models, IReadOnlyList<Measurement> measurements, LoadSample before, LoadSample after,
        OrtSessionSettings session)
    {
        var (commit, dirty) = HostInfo.Git(o.RepoRoot);
        var notes = new List<string>();
        if (dirty == true) notes.Add("The working tree had uncommitted changes when this ran, so the commit above doesn't fully describe the code measured.");
        if (o.Repeat < 2) notes.Add("Only one repeat was run, so run-to-run variation wasn't measured and SC-007 can't be judged from this report.");

        var header = new RunHeader(
            Command: "Tau.Bench " + string.Join(' ', Environment.GetCommandLineArgs().Skip(1).Select(Quote)),
            InvokedBy: o.InvokedBy,
            DateUtc: DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
            GitCommit: commit, GitDirty: dirty, ContractVersion: ReportJson.ContractVersion,
            Hardware: HostInfo.Hardware(), Software: HostInfo.Software(ortNative),
            LoadBefore: before, LoadAfter: after, ProviderCheck: check, Notes: notes);

        var types = o.Questions.ToDictionary(q => q.ToString(System.Globalization.CultureInfo.InvariantCulture), workload.Types);
        var sessionFacts = new List<string>
        {
            $"graph optimisation {session.GraphOptimizationLevel}", "sequential execution",
            $"intra-op threads {session.IntraOpThreads}", $"inter-op threads {session.InterOpThreads}", "deterministic compute on",
        };
        if (o.Provider == OrtProvider.Cuda) sessionFacts.Add("CUDA: use_tf32=0, cudnn_conv_algo_search=DEFAULT");
        sessionFacts.Add("no Tau calibrators loaded (reference post-processing)");

        return new BenchReport("latency", kind, o.Provider.Flavour(), check.Actual, o.Tag, header,
            new WorkloadInfo(workload.Name, workload.Description, workload.Sha256, workload.StateWords, types),
            new RunSettings(o.Warmup, o.Iterations, o.Repeat, o.Questions, "System.Diagnostics.Stopwatch (high resolution)",
                Stats.PercentileMethod, sessionFacts),
            models, measurements);
    }

    private static string Quote(string a) => a.Contains(' ', StringComparison.Ordinal) ? $"\"{a}\"" : a;
}

/// <summary>Model identity facts read from a package manifest.</summary>
internal static class ModelMeta
{
    public static ModelInfo FromPackage(Tau.Inference.Models.ModelPackage p) => new(
        p.Id, p.Family.ToString().ToLowerInvariant(), p.SourceRevision, p.OnnxSha256,
        (string?)p.Manifest["onnx"]?["precision"] ?? "unknown (not in tau-model.json)", p.ReferencePackage);

    /// <summary>Reads a manifest without loading or hashing the model (HTTP mode: the server already verified it).</summary>
    public static JsonObject? Manifest(string modelsDir, string id)
    {
        var path = Path.Combine(modelsDir, id, "tau-model.json");
        return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))?.AsObject() : null;
    }
}
