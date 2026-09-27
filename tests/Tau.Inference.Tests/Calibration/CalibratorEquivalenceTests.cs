using Tau.Calibration;
using Tau.Inference.Engine;
using Tau.Inference.Models;
using Tau.Inference.Onnx;
using Tau.Workbench.Calibrate;
using Tau.Workbench.Measure;
using Tau.Workbench.Spec;

namespace Tau.Inference.Tests.Calibration;

/// <summary>
/// SC-007 (T031): the same <c>tau.calibrator</c> file gives the same probabilities through the Workbench's offline
/// path (a raw answer, then <see cref="CalibrationFitter.Apply"/>, which is what <c>CalibrateStage.ApplyOffline</c>
/// does per item) and through the Runtime's engine loading it from its calibrators directory.
/// <para>
/// Two checks per item, because the two sides see different inputs (DECISIONS 2026-09-27, "Calibrators act on the
/// log of the reference probabilities"). The engine calibrates the unrounded reference vector; the Workbench only
/// sees the raw answer, rounded to 4 dp.
/// </para>
/// <list type="bullet">
/// <item>Exact: the unrounded vector the engine fed its calibrator, run through the Workbench's path, equals the
/// engine's unrounded calibrated vector within 1e-12. The trace is tied to the real answers: the raw answer is that
/// vector rounded, and the calibrated answer is the engine's output rounded.</item>
/// <item>Endpoint: the Workbench's offline result from the rounded raw answer equals the engine's calibrated answer
/// within 1e-3, on inputs that are not saturated (every raw probability at least 0.01). Saturated items still get
/// the exact check; each model and question must have at least one unsaturated item.</item>
/// </list>
/// </summary>
[Trait("Category", "Models")]
public sealed class CalibratorEquivalenceTests : IDisposable
{
    private const double ExactTolerance = 1e-12;
    private const double EndpointTolerance = 1e-3;
    private const double SaturationFloor = 0.01;

    /// <summary>Half a unit in the 4th decimal place, plus float32 slack for the engine's cast.</summary>
    private const double RoundingSlack = 5e-5 + 1e-6;

    private static readonly CalibratorFitted Fitted = new(100, "hand-made SC-007 calibrator", null, "2026-09-27", null, null, "tests", "0");

    private static readonly QuestionSpec Queue = new("queue", QuestionType.Choice, "Which team should own this ticket?",
    [
        new SpecOption("billing", "refunds, double charges, invoices"),
        new SpecOption("technical", "outages, bugs, error messages"),
        new SpecOption("account", "logins, passwords, profile changes"),
    ]);

    private static readonly QuestionSpec Refund = new("refund", QuestionType.Noul, "Is the customer asking for money back?", []);

    private static readonly string[] Texts =
    [
        "I was charged twice for my order and I want a refund today.",
        "The app keeps logging me out and now I can't reset my password either.",
        "Since the update your dashboard shows an error and my invoice page won't load.",
        "After changing my email address the billing page errors and I was charged again.",
    ];

    private readonly string _dir = Directory.CreateTempSubdirectory("tau-sc007-").FullName;
    private readonly ITestOutputHelper _out = TestContext.Current.TestOutputHelper!;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    public static TheoryData<string> Methods => new("temperature", "isotonic");

    [Theory]
    [MemberData(nameof(Methods))]
    public async Task Workbench_offline_calibration_equals_the_runtime_for_the_same_file(string method)
    {
        WriteCalibrators(method);
        using var engine = new OnnxDecisionEngine(new EngineSettings
        {
            ModelsDirectory = RepoRoot.Models,
            NativeDirectory = Path.Combine(RepoRoot.Path, "native"),
            CudaDepsDirectory = Path.Combine(RepoRoot.Path, "native", "cuda-deps"),
            Provider = OrtProvider.Cpu,
            Models = ["laya-en", "von-1.2.0"],
            Preload = false,
            CalibratorsDirectory = _dir,
        });
        var traces = new List<CalibrationTrace>();
        engine.CalibrationObserver = traces.Add;
        var workbenchSet = CalibratorSet.LoadDirectory(_dir);

        var failures = new List<string>();
        double maxExact = 0, maxEndpoint = 0;
        foreach (var (model, q) in new[] { ("laya-en", Queue), ("laya-en", Refund), ("von-1.2.0", Queue) })
        {
            var endpointCases = 0;
            var file = workbenchSet.Find(model, q.Type, q.Classes.Count)
                       ?? throw new InvalidOperationException($"no calibrator for {model} {q.Type}");
            foreach (var text in Texts)
            {
                var label = $"{method} {model} {q.Type.ToWireString()} \"{text[..24]}…\"";
                var request = MeasureStage.BuildRequest(q, model, text);

                // The Workbench's side: a raw answer (x-tau-raw: true), then the offline application.
                var raw = await engine.DecideAsync(request, new DecisionOptions(Raw: true), TestContext.Current.CancellationToken);
                Assert.Empty(raw.Diagnostics.Calibrators);
                var rawVec = MeasureStage.ExtractProbabilities(q, raw.Response, out var rawProblem)
                             ?? throw new InvalidOperationException($"{label}: raw answer unreadable: {rawProblem}");
                var offline = CalibrationFitter.Apply(file, [rawVec])[0];

                // The Runtime's side: the same request, calibrated in the engine.
                traces.Clear();
                var cal = await engine.DecideAsync(request, new DecisionOptions(), TestContext.Current.CancellationToken);
                Assert.Single(cal.Diagnostics.Calibrators);
                var calVec = MeasureStage.ExtractProbabilities(q, cal.Response, out var calProblem)
                             ?? throw new InvalidOperationException($"{label}: calibrated answer unreadable: {calProblem}");
                var trace = Assert.Single(traces);
                Assert.Equal(file.Model, trace.File.Model);
                Assert.Equal(file.QuestionType, trace.File.QuestionType);
                Assert.Equal(file.Method, trace.File.Method);

                // Exact: identical unrounded vector into both paths.
                var workbenchOnReference = CalibrationFitter.Apply(file, [trace.Reference])[0];
                for (var i = 0; i < trace.Calibrated.Length; i++)
                {
                    var d = Math.Abs(workbenchOnReference[i] - trace.Calibrated[i]);
                    maxExact = Math.Max(maxExact, d);
                    if (d > ExactTolerance) failures.Add($"{label}: exact option {i}: workbench {workbenchOnReference[i]:R} vs runtime {trace.Calibrated[i]:R}");
                }

                // The trace is what the answers were made from: raw = reference rounded, calibrated = output rounded.
                // Both families report laya-en/von choice in option order and laya noul as [1 - p, p], as the Workbench reads them.
                AssertClose(label + " raw vs reference", rawVec, trace.Reference, RoundingSlack, failures);
                AssertClose(label + " calibrated vs engine output", calVec, trace.Calibrated, RoundingSlack, failures);

                // Endpoint: what the Workbench computes from the rounded raw answer against what the Runtime answers.
                if (rawVec.Min() < SaturationFloor)
                {
                    _out.WriteLine($"{label}: saturated (min p {rawVec.Min():R}); exact check only");
                    continue;
                }

                endpointCases++;
                for (var i = 0; i < calVec.Length; i++)
                {
                    var d = Math.Abs(offline[i] - calVec[i]);
                    maxEndpoint = Math.Max(maxEndpoint, d);
                    if (d > EndpointTolerance) failures.Add($"{label}: endpoint option {i}: workbench {offline[i]:R} vs runtime {calVec[i]:R}");
                }

                _out.WriteLine($"{label}: raw [{Fmt(rawVec)}] runtime [{Fmt(calVec)}] workbench [{Fmt(offline)}]");
            }

            if (endpointCases == 0) failures.Add($"{method} {model} {q.Type.ToWireString()}: every input was saturated, so the endpoint check never ran");
        }

        _out.WriteLine($"{method}: max |Δ| exact {maxExact:E2}, endpoint {maxEndpoint:E2}");
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    private void WriteCalibrators(string method)
    {
        foreach (var (model, type) in new[] { ("laya-en", QuestionType.Choice), ("laya-en", QuestionType.Noul), ("von-1.2.0", QuestionType.Choice) })
        {
            var hash = ModelPackage.Load(Path.Combine(RepoRoot.Models, model)).OnnxSha256;
            var file = method == "temperature"
                ? CalibratorFile.CreateTemperature(model, hash, type, null, 2.0, Fitted)
                : CalibratorFile.CreateIsotonic(model, hash, type, null,
                    new IsotonicKnots([0.0, 0.1, 0.3, 0.6, 0.9, 1.0], [0.02, 0.12, 0.35, 0.6, 0.85, 0.97]), Fitted);
            file.Write(Path.Combine(_dir, $"{model}.{type.ToWireString()}.calibrator.json"));
        }
    }

    private static void AssertClose(string label, double[] actual, double[] expected, double tolerance, List<string> failures)
    {
        if (actual.Length != expected.Length)
        {
            failures.Add($"{label}: {actual.Length} options vs {expected.Length}");
            return;
        }

        for (var i = 0; i < actual.Length; i++)
            if (Math.Abs(actual[i] - expected[i]) > tolerance)
                failures.Add($"{label}: option {i}: {actual[i]:R} vs {expected[i]:R}");
    }

    private static string Fmt(double[] v) => string.Join(", ", v.Select(x => x.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)));
}
