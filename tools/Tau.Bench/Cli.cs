using System.Globalization;
using Tau.Inference.Onnx;

namespace Tau.Bench;

/// <summary>Parsed command line. One process measures one provider (ONNX Runtime loads one native flavour per process).</summary>
internal sealed record BenchOptions
{
    public string? CombineDir { get; init; }
    public OrtProvider Provider { get; init; }
    public string OutDir { get; init; } = "reports/r1";
    public IReadOnlyList<string> Models { get; init; } = [];
    public IReadOnlyList<int> Questions { get; init; } = [1, 4, 10];
    public int Warmup { get; init; } = 50;
    public int Iterations { get; init; }
    public int Repeat { get; init; } = 2;

    /// <summary>Requests per cell in the varied-inputs pass (each with a state length not seen before); 0 skips it.</summary>
    public int VariedIterations { get; init; }
    public string? Tag { get; init; }
    public Uri? HttpUrl { get; init; }
    public string? HttpServerLog { get; init; }
    public string ModelsDir { get; init; } = "";
    public string NativeDir { get; init; } = "";
    public string CudaDepsDir { get; init; } = "";
    public string? InvokedBy { get; init; }
    public string RepoRoot { get; init; } = "";

    public bool IsHttp => HttpUrl is not null;

    /// <summary>Output file stem: <c>latency-cuda</c>, <c>latency-http-cuda</c>, plus <c>-tag</c> when given.</summary>
    public string FileStem => "latency-" + (IsHttp ? "http-" : "") + Provider.Flavour() + (Tag is { Length: > 0 } t ? "-" + t : "");

    public const string Usage = """
        Tau.Bench: latency of Tau's decision models, in-process or over HTTP.

          --provider cuda|cpu|directml   execution provider (in HTTP mode: the provider the server was started with)
          --out DIR                      report directory (default reports/r1, relative to the repo root)
          --models a,b                   model ids (default: every package in the models directory)
          --questions 1,4,10             questions per request (default 1,4,10)
          --warmup N                     un-timed iterations per cell before measuring (default 50)
          --iterations N                 measured iterations per cell (default 500 on a GPU, 100 on CPU)
          --repeat N                     back-to-back repeats of the whole measured pass (default 2)
          --tag NAME                     suffix for the output files and a label in the report
          --http-url URL                 HTTP mode: time POST /v1/systemone against a running Runtime
          --http-server-log PATH         Runtime stdout log; the bench checks it names the provider
          --models-dir / --native-dir / --cuda-deps-dir   (defaults <repo>/models, <repo>/native, <repo>/native/cuda-deps;
                                          env TAU_MODELS_DIR / TAU_NATIVE_DIR / TAU_CUDA_DEPS_DIR also work)
          --invoked-by "CMD"             the command a reader should run to reproduce the report (recorded in it)
          --combine DIR                  merge DIR/latency-*.json into DIR/latency.{md,json} and exit
        """;

    public static BenchOptions Parse(string[] args)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (!a.StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"unexpected argument '{a}'");
            var eq = a.IndexOf('=', StringComparison.Ordinal);
            if (eq > 0) { map[a[2..eq]] = a[(eq + 1)..]; continue; }
            if (i + 1 >= args.Length) throw new ArgumentException($"{a} needs a value");
            map[a[2..]] = args[++i];
        }

        var known = new HashSet<string>(StringComparer.Ordinal)
        {
            "provider", "out", "models", "questions", "warmup", "iterations", "repeat", "tag", "http-url", "http-server-log", "varied-iterations",
            "models-dir", "native-dir", "cuda-deps-dir", "invoked-by", "combine",
        };
        foreach (var k in map.Keys)
            if (!known.Contains(k)) throw new ArgumentException($"unknown option --{k}");

        var root = FindRepoRoot();
        string Resolve(string p) => Path.GetFullPath(Path.IsPathRooted(p) ? p : Path.Combine(root, p));

        if (map.TryGetValue("combine", out var combine))
            return new BenchOptions { CombineDir = Resolve(combine), RepoRoot = root };

        if (!map.TryGetValue("provider", out var providerText)) throw new ArgumentException("--provider is required");
        var provider = OrtProviderNames.Parse(providerText);

        var questions = map.TryGetValue("questions", out var q)
            ? q.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Int).ToArray()
            : [1, 4, 10];
        if (questions.Any(x => x is < 1 or > 10)) throw new ArgumentException("--questions values must be 1..10 (the workload has 10)");
        if (questions.Distinct().Count() != questions.Length) throw new ArgumentException("--questions has duplicates");

        var iterations = map.TryGetValue("iterations", out var it) ? Int(it) : provider == OrtProvider.Cpu ? 100 : 500;
        var warmup = map.TryGetValue("warmup", out var w) ? Int(w) : 50;
        var repeat = map.TryGetValue("repeat", out var r) ? Int(r) : 2;
        if (iterations < 1 || warmup < 0 || repeat < 1) throw new ArgumentException("--iterations and --repeat must be >= 1, --warmup >= 0");

        var tag = map.GetValueOrDefault("tag");
        if (tag is not null && !tag.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            throw new ArgumentException("--tag may contain letters, digits, '-' and '_' only");

        Uri? http = null;
        if (map.TryGetValue("http-url", out var u))
        {
            if (!Uri.TryCreate(u, UriKind.Absolute, out http) || http.Scheme is not ("http" or "https"))
                throw new ArgumentException($"--http-url '{u}' is not an absolute http(s) URL");
        }

        var modelsDir = map.GetValueOrDefault("models-dir") ?? Environment.GetEnvironmentVariable("TAU_MODELS_DIR") ?? "models";
        var nativeDir = map.GetValueOrDefault("native-dir") ?? Environment.GetEnvironmentVariable("TAU_NATIVE_DIR") ?? "native";
        var cudaDir = map.GetValueOrDefault("cuda-deps-dir") ?? Environment.GetEnvironmentVariable("TAU_CUDA_DEPS_DIR")
            ?? Path.Combine(Resolve(nativeDir), "cuda-deps");

        return new BenchOptions
        {
            Provider = provider,
            OutDir = Resolve(map.GetValueOrDefault("out") ?? "reports/r1"),
            Models = map.TryGetValue("models", out var m)
                ? m.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : [],
            Questions = questions,
            Warmup = warmup,
            Iterations = iterations,
            Repeat = repeat,
            VariedIterations = map.TryGetValue("varied-iterations", out var vi) ? int.Parse(vi, System.Globalization.CultureInfo.InvariantCulture)
                : provider == Tau.Inference.Onnx.OrtProvider.Cpu ? 10 : 150,
            Tag = tag,
            HttpUrl = http,
            HttpServerLog = map.TryGetValue("http-server-log", out var log) ? Resolve(log) : null,
            ModelsDir = Resolve(modelsDir),
            NativeDir = Resolve(nativeDir),
            CudaDepsDir = Resolve(cudaDir),
            InvokedBy = map.GetValueOrDefault("invoked-by"),
            RepoRoot = root,
        };
    }

    private static int Int(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
        ? v
        : throw new ArgumentException($"'{s}' is not an integer");

    /// <summary>The directory holding <c>Tau.slnx</c>, searched upwards from the binary and then the working directory.</summary>
    internal static string FindRepoRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            for (var d = new DirectoryInfo(start); d is not null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "Tau.slnx"))) return d.FullName;
        return Directory.GetCurrentDirectory();
    }
}
