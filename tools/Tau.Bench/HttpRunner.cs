using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tau.Contract;
using Tau.Inference.Onnx;

namespace Tau.Bench;

/// <summary>Times <c>POST /v1/systemone</c> against a running Runtime, measured on the client side.</summary>
internal static class HttpRunner
{
    public static BenchReport Run(BenchOptions o, Workload workload, Func<LoadSample> sampleLoad)
    {
        using var http = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = Timeout.InfiniteTimeSpan, UseProxy = false })
        {
            BaseAddress = new Uri(o.HttpUrl!.AbsoluteUri.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromMinutes(2),
            DefaultRequestVersion = HttpVersion.Version11,
        };

        var health = http.GetAsync("healthz").GetAwaiter().GetResult();
        if (health.StatusCode != HttpStatusCode.OK)
            throw new BenchRefusedException($"GET {o.HttpUrl}healthz returned {(int)health.StatusCode}; the Runtime isn't ready");

        var served = JsonNode.Parse(http.GetStringAsync("v1/models").GetAwaiter().GetResult())?["models"]?.AsArray()
                     ?? throw new BenchRefusedException("GET /v1/models returned no 'models' array");
        var servedById = served.Where(m => m is not null).ToDictionary(m => (string)m!["id"]!, m => m!.AsObject());
        string[] ids = o.Models.Count > 0 ? [.. o.Models] : [.. servedById.Keys.Order(StringComparer.Ordinal)];

        var models = new List<ModelInfo>();
        foreach (var id in ids)
        {
            if (!servedById.TryGetValue(id, out var m)) throw new BenchRefusedException($"the Runtime doesn't serve '{id}'");
            var sha = (string?)m["onnx_sha256"] ?? "";
            var local = ModelMeta.Manifest(o.ModelsDir, id);
            var localSha = (string?)local?["onnx"]?["sha256"];
            if (localSha is not null && !string.Equals(localSha, sha, StringComparison.OrdinalIgnoreCase))
                throw new BenchRefusedException($"{id}: the Runtime serves onnx {sha} but {o.ModelsDir} has {localSha}");
            models.Add(new ModelInfo(id, (string?)m["family"] ?? "?", (string?)m["revision"] ?? "?", sha,
                (string?)local?["onnx"]?["precision"] ?? "unknown (package manifest not found locally)",
                (string?)local?["reference"]?["package"]));
        }

        var check = VerifyServerProvider(o);

        var bodies = ids.SelectMany(id => o.Questions.Select(q => (id, q)))
            .ToDictionary(k => k, k => Encoding.UTF8.GetBytes(workload.RequestJson(k.id, k.q)));
        foreach (var k in bodies.Keys) _ = workload.Request(k.id, k.q); // contract-validate each body before timing anything

        var perCell = new Dictionary<(string, int), (List<Summary> Wall, List<Summary> Model, int Rows, int Tokens)>();
        var loadBefore = sampleLoad();
        for (var rep = 1; rep <= o.Repeat; rep++)
        {
            foreach (var id in ids)
            foreach (var q in o.Questions)
            {
                Console.Write($"  repeat {rep}/{o.Repeat}  {id,-22} q={q,-2} ");
                var (wall, model, tokens) = Measure(http, bodies[(id, q)], id, q, o.Warmup, o.Iterations);
                if (!perCell.TryGetValue((id, q), out var acc)) perCell[(id, q)] = acc = ([], [], -1, tokens);
                acc.Wall.Add(Stats.Summarise(wall, q));
                acc.Model.Add(Stats.Summarise(model, q));
                Console.WriteLine($"wall p50 {acc.Wall[^1].P50:0.00} ms  x-tau-model-ms p50 {acc.Model[^1].P50:0.00} ms");
            }
        }
        var loadAfter = sampleLoad();

        var wallCells = new List<Cell>();
        var modelCells = new List<Cell>();
        foreach (var id in ids)
        foreach (var q in o.Questions)
        {
            var c = perCell[(id, q)];
            wallCells.Add(new Cell(id, q, c.Rows, c.Tokens, c.Wall, Stats.RelativeDiffPercent(c.Wall[0].P50, c.Wall.ElementAtOrDefault(1)?.P50)));
            modelCells.Add(new Cell(id, q, c.Rows, c.Tokens, c.Model, Stats.RelativeDiffPercent(c.Model[0].P50, c.Model.ElementAtOrDefault(1)?.P50)));
        }

        var measurements = new[]
        {
            new Measurement("http_end_to_end", "HTTP end to end",
                "Client-side wall time for one POST /v1/systemone over a kept-alive localhost connection: sending the JSON body, "
                + "the Runtime's parsing, validation and inference, and reading and deserialising the response into the contract types.",
                wallCells),
            new Measurement("http_model_header", "Model forward pass as reported by the Runtime",
                "The x-tau-model-ms response header of the same requests: the forward-pass time measured inside the server. "
                + "The gap between this and the HTTP figure is what HTTP, JSON and the host add.", modelCells),
        };

        var report = InProcessRunner.Assemble(o, workload, "http", check, null, models, measurements, loadBefore, loadAfter,
            new OrtSessionSettings());
        return report with
        {
            Header = report.Header with
            {
                Notes = [.. report.Header.Notes, $"Server: {o.HttpUrl}. Batch rows aren't visible over HTTP, so they're recorded as -1; input tokens come from the response's usage block."],
            },
        };
    }

    private static (List<double> Wall, List<double> Model, int Tokens) Measure(
        HttpClient http, byte[] body, string id, int q, int warmup, int iterations)
    {
        var tokens = 0;
        for (var i = 0; i < Math.Max(1, warmup); i++)
        {
            var (_, _, t) = Post(http, body, id, q);
            tokens = t;
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var wall = new List<double>(iterations);
        var model = new List<double>(iterations);
        for (var i = 0; i < iterations; i++)
        {
            var (w, m, _) = Post(http, body, id, q);
            wall.Add(w);
            model.Add(m);
        }
        return (wall, model, tokens);
    }

    private static (double WallMs, double ModelMs, int Tokens) Post(HttpClient http, byte[] body, string id, int q)
    {
        var t0 = Stopwatch.GetTimestamp();
        using var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        using var res = http.PostAsync("v1/systemone", content).GetAwaiter().GetResult();
        var bytes = res.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
        var parsed = res.IsSuccessStatusCode ? JsonSerializer.Deserialize<DecisionResponse>(bytes, ContractJson.Options) : null;
        var wall = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;

        if (parsed is null)
            throw new BenchRefusedException($"{id} q={q}: HTTP {(int)res.StatusCode}: {Encoding.UTF8.GetString(bytes)}");
        if (parsed.Model != id || parsed.Answers.Count != q)
            throw new BenchRefusedException($"{id} q={q}: the Runtime answered with model '{parsed.Model}' and {parsed.Answers.Count} answers");
        if (res.Headers.TryGetValues("x-tau-truncated", out var tr) && tr.FirstOrDefault() == "true")
            throw new BenchRefusedException($"{id} q={q}: the workload state was truncated");
        if (!res.Headers.TryGetValues("x-tau-model-ms", out var mh)
            || !double.TryParse(mh.FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out var modelMs))
            throw new BenchRefusedException($"{id} q={q}: no parseable x-tau-model-ms header; is this a Tau Runtime?");
        return (wall, modelMs, parsed.Usage?.InputTokens ?? 0);
    }

    /// <summary>
    /// The server doesn't expose its provider over HTTP, so the bench reads the Runtime's start-up log line
    /// ("Tau engine ready: provider Cuda, ...") written from <c>engine.ActualProvider</c>.
    /// </summary>
    private static ProviderCheck VerifyServerProvider(BenchOptions o)
    {
        var want = o.Provider switch
        {
            OrtProvider.Cuda => "Cuda",
            OrtProvider.DirectMl => "DirectMl",
            _ => "Cpu",
        };
        if (o.HttpServerLog is null)
            return new ProviderCheck(o.Provider.Flavour(), "unverified", false,
                ["no --http-server-log given: the provider is what the caller says the server was started with, not verified"]);

        string log;
        using (var fs = new FileStream(o.HttpServerLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var sr = new StreamReader(fs))
            log = sr.ReadToEnd();

        var marker = "Tau engine ready: provider ";
        var at = log.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0)
            throw new BenchRefusedException($"{o.HttpServerLog} has no '{marker}...' line, so the server's provider can't be confirmed");
        var actual = new string(log[(at + marker.Length)..].TakeWhile(char.IsLetter).ToArray());
        if (actual != want)
            throw new BenchRefusedException($"the Runtime log says provider {actual}, but this run is labelled {o.Provider.Flavour()}. Refusing to publish.");
        return new ProviderCheck(o.Provider.Flavour(), o.Provider.Flavour(), true,
        [
            $"the Runtime's start-up log ({Path.GetFileName(o.HttpServerLog)}) says 'Tau engine ready: provider {actual}', logged from engine.ActualProvider",
            "scripts/bench.ps1 starts the Runtime with --Tau:AllowCpuFallback=false, so a GPU that can't start stops the server instead of falling back",
        ]);
    }
}
