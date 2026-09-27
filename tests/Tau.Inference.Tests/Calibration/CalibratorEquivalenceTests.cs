using Tau.Calibration;
using Tau.Contract;
using Tau.Inference.Engine;
using Tau.Inference.Models;
using Tau.Inference.Onnx;
using Tau.Inference.PostProcessing;
using Tau.Workbench.Calibrate;
using Tau.Workbench.Measure;
using Tau.Workbench.Spec;

namespace Tau.Inference.Tests.Calibration;

/// <summary>
/// SC-007 (T031): the same <c>tau.calibrator</c> file gives the same probabilities through the Workbench's offline
/// path (a raw answer, then <see cref="CalibrationFitter.Apply"/>, which is what <c>CalibrateStage.ApplyOffline</c>
/// does per item) and through the Runtime's engine loading it from its calibrators directory.
/// <para>
/// The Workbench asks for <c>x-tau-precision: full</c>, so its raw answer carries the unrounded vector the engine
/// feeds its calibrator (DECISIONS 2026-09-27, "Calibrators were fitted on rounded probabilities"). The inputs are
/// realistic: a 77-option Banking77 question on laya-en and von-1.2.0, where nearly every probability rounds to 0 at
/// 4 dp, as well as a 3-option choice and laya-en noul. No item is skipped for being saturated.
/// </para>
/// <list type="bullet">
/// <item>Shared vector: the vector the engine calibrated, run through the Workbench's path, equals the engine's output
/// within 1e-12, and it is exactly the vector the Workbench reads from the full-precision raw answer.</item>
/// <item>Endpoint: the Workbench's offline calibration of the full-precision raw answer equals the Runtime's
/// full-precision calibrated answer within 1e-9.</item>
/// <item>Rounding: the Runtime's ordinary (rounded) calibrated answer is that full-precision answer rounded to 4 dp.</item>
/// </list>
/// A second test shows the gap the header closes: fitted and applied on the rounded raw answer, the two differ.
/// </summary>
[Trait("Category", "Models")]
public sealed class CalibratorEquivalenceTests : IDisposable
{
    private const double SharedTolerance = 1e-12;
    private const double EndpointTolerance = 1e-9;
    private const double SaturationFloor = 0.01;

    private static readonly CalibratorFitted Fitted = new(100, "hand-made SC-007 calibrator", null, "2026-09-27", null, null, "tests", "0");

    private static readonly QuestionSpec Queue = new("queue", QuestionType.Choice, "Which team should own this ticket?",
    [
        new SpecOption("billing", "refunds, double charges, invoices"),
        new SpecOption("technical", "outages, bugs, error messages"),
        new SpecOption("account", "logins, passwords, profile changes"),
    ]);

    private static readonly QuestionSpec Refund = new("refund", QuestionType.Noul, "Is the customer asking for money back?", []);

    /// <summary>The Banking77 worked example's 77-option intent question, as its spec defines it.</summary>
    private static readonly QuestionSpec Banking77 =
        DecisionSpec.Load(Path.Combine(RepoRoot.Path, "examples", "banking77", "decision.yaml")).Question;

    private static readonly string[] TicketTexts =
    [
        "I was charged twice for my order and I want a refund today.",
        "The app keeps logging me out and now I can't reset my password either.",
        "Since the update your dashboard shows an error and my invoice page won't load.",
        "After changing my email address the billing page errors and I was charged again.",
    ];

    private static readonly string[] BankingTexts =
    [
        "I ordered a new card two weeks ago and it still hasn't arrived.",
        "Why was I charged a fee when I paid by card abroad?",
        "My top up failed but the money has already left my bank account.",
        "There are payments on my statement I don't recognise, I think my card has been compromised.",
        "How do I change my PIN?",
        "The exchange rate I got for my cash withdrawal looks wrong.",
    ];

    private readonly string _dir = Directory.CreateTempSubdirectory("tau-sc007-").FullName;
    private readonly ITestOutputHelper _out = TestContext.Current.TestOutputHelper!;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>Hand-made temperature and isotonic calibrators, and the ones fitted for the Banking77 example.</summary>
    public static TheoryData<string> Sets => new("temperature", "isotonic", "banking77");

    [Theory]
    [MemberData(nameof(Sets))]
    public async Task Workbench_offline_calibration_of_the_full_precision_raw_answer_equals_the_runtime(string set)
    {
        WriteCalibrators(set);
        using var engine = Engine();
        var traces = new List<CalibrationTrace>();
        engine.CalibrationObserver = traces.Add;
        var workbenchSet = CalibratorSet.LoadDirectory(_dir);
        var ct = TestContext.Current.CancellationToken;

        var failures = new List<string>();
        double maxShared = 0, maxEndpoint = 0;
        var saturated = 0;
        var items = 0;
        foreach (var (model, q, texts) in Cases(set))
        {
            var file = workbenchSet.Find(model, q.Type, q.Classes.Count)
                       ?? throw new InvalidOperationException($"no calibrator for {model} {q.Type}");
            foreach (var text in texts)
            {
                items++;
                var label = $"{set} {model} {q.Type.ToWireString()}/{q.Classes.Count} \"{text[..Math.Min(24, text.Length)]}…\"";
                var request = MeasureStage.BuildRequest(q, model, text);

                // The Workbench's side: a full-precision raw answer, then the offline application.
                var raw = await engine.DecideAsync(request, new DecisionOptions(Raw: true, FullPrecision: true), ct);
                Assert.Empty(raw.Diagnostics.Calibrators);
                var rawVec = Vector(q, raw.Response, label);
                var offline = CalibrationFitter.Apply(file, [rawVec])[0];
                if (rawVec.Min() < SaturationFloor) saturated++;

                // The Runtime's side: the same request, calibrated in the engine, at full precision.
                traces.Clear();
                var cal = await engine.DecideAsync(request, new DecisionOptions(FullPrecision: true), ct);
                Assert.Single(cal.Diagnostics.Calibrators);
                var calVec = Vector(q, cal.Response, label);
                var trace = Assert.Single(traces);
                Assert.Equal(file.Model, trace.File.Model);
                Assert.Equal(file.QuestionType, trace.File.QuestionType);
                Assert.Equal(file.Method, trace.File.Method);
                Assert.Equal(file.Bucket, trace.File.Bucket);

                // Shared vector: the Workbench now reads exactly the vector the engine calibrated, and both paths
                // compute the same output from it.
                AssertClose(label + " raw answer vs engine input", rawVec, trace.Reference, 0, failures);
                AssertClose(label + " calibrated answer vs engine output", calVec, trace.Calibrated, SharedTolerance, failures);
                var workbenchOnReference = CalibrationFitter.Apply(file, [trace.Reference])[0];
                maxShared = Math.Max(maxShared, MaxDiff(workbenchOnReference, trace.Calibrated));
                AssertClose(label + " shared vector", workbenchOnReference, trace.Calibrated, SharedTolerance, failures);

                // Endpoint: what the Workbench computes from the raw answer against what the Runtime answers.
                maxEndpoint = Math.Max(maxEndpoint, MaxDiff(offline, calVec));
                AssertClose(label + " endpoint", offline, calVec, EndpointTolerance, failures);

                // Rounding: without the header the Runtime answers the same values rounded to 4 dp.
                var rounded = await engine.DecideAsync(request, new DecisionOptions(), ct);
                AssertRoundedTo4Dp(label, cal.Response.Answers[q.Key], rounded.Response.Answers[q.Key], failures);
            }
        }

        _out.WriteLine($"{set}: {items} items, {saturated} saturated (some raw p < {SaturationFloor}); max |Δ| shared {maxShared:E2}, endpoint {maxEndpoint:E2}");
        Assert.True(saturated > 0, "the realistic inputs should include saturated items");
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public async Task Without_full_precision_the_offline_calibration_differs_from_the_runtime()
    {
        // The bug this header fixes: the Workbench used to fit and apply on the 4-dp raw answer, where most of the
        // 77 probabilities are 0 and get floored at 1e-6, while the Runtime applies the calibrator to the unrounded
        // values. With the Banking77 example's own calibrators the two disagree.
        WriteCalibrators("banking77");
        using var engine = Engine();
        var set = CalibratorSet.LoadDirectory(_dir);
        var ct = TestContext.Current.CancellationToken;
        foreach (var model in new[] { "laya-en", "von-1.2.0" })
        {
            var file = set.Find(model, QuestionType.Choice, Banking77.Classes.Count)!;
            double gap = 0;
            foreach (var text in BankingTexts)
            {
                var request = MeasureStage.BuildRequest(Banking77, model, text);
                var roundedRaw = await engine.DecideAsync(request, new DecisionOptions(Raw: true), ct);
                var offline = CalibrationFitter.Apply(file, [Vector(Banking77, roundedRaw.Response, text)])[0];
                var runtime = await engine.DecideAsync(request, new DecisionOptions(FullPrecision: true), ct);
                gap = Math.Max(gap, MaxDiff(offline, Vector(Banking77, runtime.Response, text)));
            }

            _out.WriteLine($"{model}: rounded raw answers, max |offline − runtime| {gap:E2}");
            Assert.True(gap > EndpointTolerance, $"{model}: expected the rounded raw answer to give a different calibration, max gap {gap:E2}");
        }
    }

    private OnnxDecisionEngine Engine() => new(new EngineSettings
    {
        ModelsDirectory = RepoRoot.Models,
        NativeDirectory = Path.Combine(RepoRoot.Path, "native"),
        CudaDepsDirectory = Path.Combine(RepoRoot.Path, "native", "cuda-deps"),
        Provider = OrtProvider.Cpu,
        Models = ["laya-en", "von-1.2.0"],
        Preload = false,
        CalibratorsDirectory = _dir,
    });

    private static IEnumerable<(string Model, QuestionSpec Question, string[] Texts)> Cases(string set) => set == "banking77"
        ? [("laya-en", Banking77, BankingTexts), ("von-1.2.0", Banking77, BankingTexts)]
        :
        [
            ("laya-en", Queue, TicketTexts), ("laya-en", Refund, TicketTexts), ("von-1.2.0", Queue, TicketTexts),
            ("laya-en", Banking77, BankingTexts), ("von-1.2.0", Banking77, BankingTexts),
        ];

    private void WriteCalibrators(string set)
    {
        if (set == "banking77")
        {
            // The calibrators the Banking77 example fitted (laya-en temperature T ≈ 3.05, von-1.2.0 isotonic).
            foreach (var model in new[] { "laya-en", "von-1.2.0" })
                foreach (var f in Directory.EnumerateFiles(Path.Combine(RepoRoot.Path, "examples", "banking77", "calibrators", model), "*.calibrator.json"))
                    File.Copy(f, Path.Combine(_dir, Path.GetFileName(f)));
            return;
        }

        foreach (var (model, type) in new[] { ("laya-en", QuestionType.Choice), ("laya-en", QuestionType.Noul), ("von-1.2.0", QuestionType.Choice) })
        {
            var hash = ModelPackage.Load(Path.Combine(RepoRoot.Models, model)).OnnxSha256;
            var file = set == "temperature"
                ? CalibratorFile.CreateTemperature(model, hash, type, null, 2.0, Fitted)
                : CalibratorFile.CreateIsotonic(model, hash, type, null,
                    new IsotonicKnots([0.0, 0.1, 0.3, 0.6, 0.9, 1.0], [0.02, 0.12, 0.35, 0.6, 0.85, 0.97]), Fitted);
            file.Write(Path.Combine(_dir, $"{model}.{type.ToWireString()}.calibrator.json"));
        }
    }

    private static double[] Vector(QuestionSpec q, DecisionResponse response, string label) =>
        MeasureStage.ExtractProbabilities(q, response, out var problem)
        ?? throw new InvalidOperationException($"{label}: answer unreadable: {problem}");

    /// <summary>Every value the rounded answer reports is the full-precision answer's value rounded to 4 dp.</summary>
    private static void AssertRoundedTo4Dp(string label, Answer full, Answer rounded, List<string> failures)
    {
        void Check(string what, double f, double r)
        {
            if (r != Numerics.PyRound(f, 4)) failures.Add($"{label}: rounded {what} {r:R} is not {f:R} to 4 dp");
        }

        switch (full, rounded)
        {
            case (ChoiceAnswer f, ChoiceAnswer r):
                foreach (var (key, p) in f.Probabilities) Check(key, p, r.Probabilities[key]);
                break;
            case (NoulAnswer f, NoulAnswer r):
                Check("noul", f.Noul, r.Noul);
                break;
            default:
                failures.Add($"{label}: answer types {full.Type} and {rounded.Type}");
                break;
        }
    }

    private static double MaxDiff(double[] a, double[] b) => a.Zip(b, (x, y) => Math.Abs(x - y)).Max();

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
}
