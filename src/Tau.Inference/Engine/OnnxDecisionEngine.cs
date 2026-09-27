using Tau.Calibration;
using Tau.Contract;
using Tau.Inference.Laya;
using Tau.Inference.Models;
using Tau.Inference.Onnx;
using Tau.Inference.PostProcessing;
using Tau.Inference.Routing;
using Tau.Inference.Von;

namespace Tau.Inference.Engine;

/// <summary>Engine configuration.</summary>
public sealed record EngineSettings
{
    /// <summary>Directory with one sub-directory per model package.</summary>
    public required string ModelsDirectory { get; init; }

    /// <summary>Directory holding <c>native/&lt;flavour&gt;/&lt;rid&gt;</c>.</summary>
    public required string NativeDirectory { get; init; }

    /// <summary>CUDA 12 / cuDNN 9 library directory (CUDA only).</summary>
    public string? CudaDepsDirectory { get; init; }

    /// <summary>Execution provider.</summary>
    public OrtProvider Provider { get; init; } = OrtProvider.Cpu;

    /// <summary>Fall back to CPU if the GPU provider can't start.</summary>
    public bool AllowCpuFallback { get; init; }

    /// <summary>Model ids to serve; empty for every package found.</summary>
    public IReadOnlyList<string> Models { get; init; } = [];

    /// <summary>Load every model's session at construction.</summary>
    public bool Preload { get; init; } = true;

    /// <summary>Directory of <c>*.calibrator.json</c> files, or null.</summary>
    public string? CalibratorsDirectory { get; init; }

    /// <summary>Von packed-sequence cap.</summary>
    public int VonMaxTokens { get; init; } = 4096;

    /// <summary>Auto-route aliases (<c>jev-*</c> always applies).</summary>
    public IReadOnlyList<string>? Aliases { get; init; }

    /// <summary>ONNX Runtime session settings.</summary>
    public OrtSessionSettings Session { get; init; } = new();
}

/// <summary>
/// The ONNX decision engine: route → build the model's rows → one batched forward pass → the model's reference
/// post-processing (or a Tau calibrator) → contract answers. Model packages are hash-verified and sessions created
/// at start-up, so a bad model, provider or calibrator stops the Runtime before it serves anything.
/// </summary>
public sealed class OnnxDecisionEngine : IDecisionEngine, IDisposable
{
    private readonly EngineSettings _settings;
    private readonly Dictionary<string, ModelPackage> _packages;
    private readonly Dictionary<string, Lazy<OnnxModel>> _models;
    private readonly ModelRouter _router;
    private readonly IReadOnlySet<string> _installed;
    private readonly CalibratorSet? _calibrators;

    /// <summary>Loads and verifies every configured package, its calibrators and (with preload) its session.</summary>
    /// <param name="settings">Settings.</param>
    public OnnxDecisionEngine(EngineSettings settings)
    {
        _settings = settings;
        OrtNativeResolver.Configure(settings.Provider, settings.NativeDirectory, settings.CudaDepsDirectory);

        var dirs = Directory.Exists(settings.ModelsDirectory)
            ? Directory.GetDirectories(settings.ModelsDirectory).Where(d => File.Exists(Path.Combine(d, "tau-model.json")))
            : [];
        var all = dirs.Select(ModelPackage.Load).ToDictionary(p => p.Id);
        var wanted = settings.Models.Count > 0 ? settings.Models : all.Keys.OrderBy(x => x, StringComparer.Ordinal).ToList();
        foreach (var id in wanted)
            if (!all.ContainsKey(id))
                throw new ModelPackageException(Path.Combine(settings.ModelsDirectory, id, "tau-model.json"),
                    "configured model not found; run scripts/export.ps1");
        _packages = wanted.ToDictionary(id => id, id => all[id]);
        if (_packages.Count == 0)
            throw new ModelPackageException(settings.ModelsDirectory, "no model packages found; run scripts/export.ps1");

        _installed = _packages.Keys.ToHashSet(StringComparer.Ordinal);
        _router = new ModelRouter(settings.Aliases);
        _models = _packages.ToDictionary(kv => kv.Key, kv => new Lazy<OnnxModel>(
            () => OnnxModel.Load(kv.Value, settings.Provider, settings.Session with { AllowCpuFallback = settings.AllowCpuFallback }),
            LazyThreadSafetyMode.ExecutionAndPublication));

        if (settings.CalibratorsDirectory is { Length: > 0 } calDir)
        {
            _calibrators = CalibratorSet.LoadDirectory(calDir);
            _calibrators.Validate(m => _packages.TryGetValue(m, out var p) ? p.OnnxSha256 : null);
        }

        if (settings.Preload)
            foreach (var m in _models.Values) _ = m.Value;
    }

    /// <inheritdoc />
    public IReadOnlyList<InstalledModel> Models => _packages.Values.Select(p => new InstalledModel(
        p.Id, p.Family == ModelFamily.Laya ? "laya" : "von", p.SourceRevision, p.OnnxSha256, _models[p.Id].IsValueCreated)).ToArray();

    /// <inheritdoc />
    public IReadOnlyList<string> Aliases => [.. (_settings.Aliases ?? ModelRouter.DefaultAliases), "jev-*"];

    /// <inheritdoc />
    public bool Ready => _models.Values.All(m => !_settings.Preload || m.IsValueCreated);

    /// <summary>The provider the loaded sessions actually run on (after any fallback).</summary>
    public OrtProvider? ActualProvider => _models.Values.FirstOrDefault(m => m.IsValueCreated)?.Value.Session.Provider;

    /// <summary>A loaded model by id (for the benchmark and tests).</summary>
    /// <param name="id">Tau model id.</param>
    public OnnxModel Model(string id) => _models[id].Value;

    /// <inheritdoc />
    public Task<DecisionResult> DecideAsync(DecisionRequest request, DecisionOptions options, CancellationToken cancellationToken)
    {
        RouteDecision route;
        try
        {
            route = _router.Resolve(request.Model, request.State, _installed);
        }
        catch (UnknownModelException e)
        {
            throw new DecisionRejectedException("model",
                $"unknown model '{e.Requested}'; installed: {string.Join(", ", e.Installed)}; aliases: {string.Join(", ", Aliases)}");
        }
        catch (InvalidOperationException e)
        {
            throw new DecisionRejectedException("model", e.Message);
        }

        var model = _models[route.ModelId].Value;
        cancellationToken.ThrowIfCancellationRequested();
        var result = model.Package.Family == ModelFamily.Laya
            ? DecideLaya(model, request, options, route)
            : DecideVon(model, request, options, route);
        return Task.FromResult(result);
    }

    private DecisionResult DecideLaya(OnnxModel model, DecisionRequest request, DecisionOptions options, RouteDecision route)
    {
        var questions = request.Questions.Select(kv => LayaSequenceBuilder.Normalise(kv.Key, kv.Value)).ToArray();
        var builder = new LayaSequenceBuilder(model.Tokenizer, model.Package.LayaLimits!);
        var rows = builder.Build(request.State, questions);
        var logits = model.RunLaya(rows, out var elapsed);
        var post = new LayaPostProcessor(model.Package.LayaPost!);

        var answers = new OrderedDictionary<string, Answer>();
        var applied = new List<string>();
        for (var i = 0; i < rows.Count; i++)
        {
            var q = rows[i].Question;
            var cal = Calibrate(model.Package.Id, q.Type, logits[i], options, applied);
            answers[q.Key] = cal is null
                ? post.Decode(q.Type, q.Labels, logits[i])
                : post.FromProbabilities(q.Type, q.Labels, cal, Numerics.ArgMaxTie(cal));
        }

        var tokens = rows.Sum(r => r.InputIds.Length);
        return Result(model, route, answers, applied, rows.Any(r => r.Truncated), rows.Count, tokens, elapsed);
    }

    private DecisionResult DecideVon(OnnxModel model, DecisionRequest request, DecisionOptions options, RouteDecision route)
    {
        var problems = new List<ValidationProblem>();
        var questions = request.Questions.Select(kv => VonSequenceBuilder.Normalise(kv.Key, kv.Value, problems)).ToArray();
        if (problems.Count > 0) throw new DecisionRejectedException(problems);

        var builder = new VonSequenceBuilder(model.Tokenizer, model.Package.VonLimits!, model.Package.VonPost!, _settings.VonMaxTokens);
        var stateText = VonSequenceBuilder.FormatState(request.State);
        var stateTokens = builder.StateTokens(stateText);
        var rows = builder.Build(stateText, questions!);
        var logits = model.RunVon(builder, rows, out var elapsed, out _);
        var post = new VonPostProcessor(model.Package.VonPost!);

        var answers = new OrderedDictionary<string, Answer>();
        var applied = new List<string>();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var q = row.Question;
            switch (row.Kind)
            {
                case VonRowKind.Choice:
                {
                    var cal = Calibrate(model.Package.Id, "choice", logits[i], options, applied);
                    answers[q.Key] = cal is null
                        ? post.Choice(q.Labels, logits[i], stateTokens)
                        : VonPostProcessor.FromChoiceProbabilities(q.Labels, cal, Numerics.ArgMaxTie(cal));
                    break;
                }
                case VonRowKind.Score:
                {
                    var cal = Calibrate(model.Package.Id, "score", logits[i], options, applied);
                    answers[q.Key] = cal is null
                        ? post.Score(q.Labels, logits[i], stateTokens)
                        : VonPostProcessor.FromScoreProbabilities(q.Labels, cal);
                    break;
                }
                case VonRowKind.Noul:
                {
                    var hasNull = i + 1 < rows.Count && rows[i + 1].Kind == VonRowKind.NoulNull;
                    var nullLogits = hasNull ? logits[i + 1] : [];
                    // A calibrator for Von noul is fitted on [true, false] logits after the reference's prior correction.
                    var corrected = post.CorrectNoul(logits[i], nullLogits);
                    var cal = Calibrate(model.Package.Id, "noul", corrected, options, applied);
                    answers[q.Key] = cal is null
                        ? post.Noul(logits[i], nullLogits, stateTokens)
                        : new NoulAnswer { Noul = Numerics.PyRound(Math.Clamp((double)cal[0], 0.0, 1.0), 4) };
                    if (hasNull) i++;
                    break;
                }
            }
        }

        var tokens = rows.Sum(r => r.InputIds.Length);
        return Result(model, route, answers, applied, false, rows.Count, tokens, elapsed);
    }

    /// <summary>Tau calibrator probabilities for one question, or null to use the reference post-processing.</summary>
    private float[]? Calibrate(string model, string type, float[] logits, DecisionOptions options, List<string> applied)
    {
        if (options.Raw || _calibrators is null) return null;
        var qt = QuestionTypeExtensions.FromWireString(type) ?? throw new ArgumentException(type);
        if (_calibrators.Find(model, qt, logits.Length) is not { } file) return null;
        var p = new Calibrator(file).Apply(logits.Select(x => (double)x).ToArray());
        var id = file.Bucket is { } b ? $"{file.Model}:{file.QuestionType.ToWireString()}:{b.ToWireString()}" : $"{file.Model}:{file.QuestionType.ToWireString()}";
        if (!applied.Contains(id)) applied.Add(id);
        return p.Select(x => (float)x).ToArray();
    }

    private static DecisionResult Result(OnnxModel model, RouteDecision route, OrderedDictionary<string, Answer> answers,
        List<string> applied, bool truncated, int rows, int tokens, TimeSpan elapsed) =>
        new(new DecisionResponse
        {
            Model = model.Package.Id, Answers = answers, Usage = new Usage { InputTokens = tokens, OutputTokens = 0 },
        }, new DecisionDiagnostics(model.Package.Id, model.Package.OnnxSha256, route.Reason, applied, truncated, rows, tokens,
            elapsed.TotalMilliseconds));

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var m in _models.Values.Where(m => m.IsValueCreated)) m.Value.Dispose();
    }
}
