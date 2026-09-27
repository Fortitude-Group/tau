using Tau.Calibration;
using Tau.Workbench.Calibrate;
using Tau.Workbench.Measure;

namespace Tau.Workbench.Tests;

public sealed class CalibrateTests
{
    [Theory]
    [InlineData(2.0, 4)]
    [InlineData(3.0, 10)]
    [InlineData(0.6, 5)]
    public void TemperatureRecoversAKnownOverconfidence(double t, int classes)
    {
        var (probs, gold) = Synthetic.Overconfident(6000, classes, t, seed: 11);
        var fit = CalibrationFitter.Fit(probs, gold, "m", StubSystemOne.Hash, QuestionType.Choice, null, "demo", null, "2026-09-27");
        Assert.InRange(fit.Temperature.Temperature!.Value, t - 0.1, t + 0.1);
        Assert.True(fit.Temperature.CalibrationLogLoss < fit.LogLossBefore);
    }

    [Fact]
    public void IsotonicKnotsAreStrictlyAscendingMonotoneAndAnchored()
    {
        var x = new double[] { 0.5, 0.5, 0.6, 0.6, 0.6, 0.9, 0.9, 0.95 };
        var y = new double[] { 1, 0, 0, 1, 1, 1, 0, 1 };
        var knots = CalibrationFitter.CollapseKnots(IsotonicRegression.Fit(x, y));
        Assert.Equal(0, knots.X[0]);
        Assert.Equal(0.95, knots.X[^1]);
        for (int i = 1; i < knots.X.Count; i++)
        {
            Assert.True(knots.X[i] > knots.X[i - 1]);
            Assert.True(knots.Y[i] >= knots.Y[i - 1]);
        }

        Assert.Equal(0, knots.Y[0]);
        Assert.All(knots.Y, v => Assert.InRange(v, 0, 1));
        // The collapsed knots must be accepted by the shared calibrator format.
        CalibratorFile.CreateIsotonic("m", StubSystemOne.Hash, QuestionType.Choice, null, knots, new CalibratorFitted(8, "d", null, "x", null, null, "t", "1"));
    }

    [Fact]
    public void DroppingInteriorKnotsKeepsTheSameFunction()
    {
        var rng = new Random(3);
        var x = Enumerable.Range(0, 2000).Select(_ => rng.NextDouble()).ToArray();
        var y = x.Select(v => rng.NextDouble() < v ? 1.0 : 0.0).ToArray();
        var fit = IsotonicRegression.Fit(x, y);
        var knots = CalibrationFitter.CollapseKnots(fit);
        Assert.True(knots.X.Count < 200, $"expected far fewer knots than points, got {knots.X.Count}");
        var compressed = IsotonicRegression.FromKnots(knots.X, knots.Y);
        for (double p = fit.X[0]; p <= 1; p += 0.0005)
        {
            Assert.Equal(fit.Apply(p), compressed.Apply(p), 12);
        }
    }

    [Fact]
    public void IsotonicIsFittedOnEveryOptionNotJustTheChosenOne()
    {
        var (probs, gold) = Synthetic.Overconfident(1000, 4, 2.5, seed: 8);
        var fit = CalibrationFitter.Fit(probs, gold, "m", StubSystemOne.Hash, QuestionType.Choice, null, "demo", null, "2026-09-27");
        // Fitted one-vs-rest, isotonic is a sensible calibrator: better than raw, not a collapse.
        Assert.True(fit.Isotonic!.CalibrationLogLoss < fit.LogLossBefore);
        Assert.True(fit.Isotonic.CalibrationEce < fit.EceBefore);
    }

    [Fact]
    public void CollapsingAveragesTiesInX()
    {
        var knots = CalibrationFitter.CollapseKnots(IsotonicRegression.Fit([0, 0, 1, 1], [0, 1, 1, 1]));
        Assert.Equal([0, 1], knots.X);
        Assert.Equal(0.5, knots.Y[0], 12);
        Assert.Equal(1, knots.Y[1], 12);
    }

    [Fact]
    public void BothMethodsAreScoredAndTheLowerLogLossIsChosen()
    {
        var (probs, gold) = Synthetic.Overconfident(2000, 4, 2.5, seed: 3);
        var fit = CalibrationFitter.Fit(probs, gold, "m", StubSystemOne.Hash, QuestionType.Choice, null, "demo", "rev", "2026-09-27");
        Assert.NotNull(fit.Isotonic);
        Assert.Null(fit.IsotonicSkipped);
        var expected = fit.Isotonic!.CalibrationLogLoss < fit.Temperature.CalibrationLogLoss ? CalibrationMethod.Isotonic : CalibrationMethod.Temperature;
        Assert.Equal(expected, fit.File.Method);
        Assert.Equal(2000, fit.File.Fitted.N);
        Assert.Equal(fit.EceBefore, fit.File.Fitted.EceBefore);
        Assert.Equal(expected == CalibrationMethod.Isotonic ? fit.Isotonic.CalibrationEce : fit.Temperature.CalibrationEce, fit.File.Fitted.EceAfter);
        Assert.Equal("tau-workbench", fit.File.Fitted.Tool);
    }

    [Fact]
    public void FewerThan200ItemsFallBackToTemperature()
    {
        var (probs, gold) = Synthetic.Overconfident(150, 4, 2, seed: 5);
        var fit = CalibrationFitter.Fit(probs, gold, "m", StubSystemOne.Hash, QuestionType.Choice, null, "demo", null, "2026-09-27");
        Assert.Null(fit.Isotonic);
        Assert.Equal(CalibrationMethod.Temperature, fit.File.Method);
        Assert.Contains("only 150 calibration items", fit.IsotonicSkipped, StringComparison.Ordinal);
    }

    [Fact]
    public void IdenticalProbabilitiesSkipIsotonic()
    {
        var probs = Enumerable.Range(0, 300).Select(_ => new[] { 0.5, 0.5 }).ToArray();
        var gold = Enumerable.Range(0, 300).Select(i => i % 3 == 0 ? 1 : 0).ToArray();
        var fit = CalibrationFitter.Fit(probs, gold, "m", StubSystemOne.Hash, QuestionType.Noul, null, "demo", null, "2026-09-27");
        Assert.Null(fit.Isotonic);
        Assert.Contains("same, so no isotonic", fit.IsotonicSkipped, StringComparison.Ordinal);
    }

    [Fact]
    public void CalibrateWritesLoadableCalibratorsAndAnOfflineView()
    {
        using var repo = TestRepo.Create(calibration: 400, heldOut: 300);
        Synthetic.WriteRawRuns(repo, "model-a", overconfidence: 2.5, seed: 1);
        var summary = CalibrateStage.Run(repo.Spec, "model-a", repo.Manifest, () => new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero));
        Assert.Equal(StubSystemOne.Hash, summary.ModelHash);
        Assert.Equal(2, summary.Calibrators.Count);
        Assert.Equal("question type", summary.Calibrators[0].Scope);
        Assert.Equal("3-5 options", summary.Calibrators[1].Scope);
        var set = CalibratorSet.LoadDirectory(repo.Spec.CalibratorsDirectory("model-a"));
        Assert.Equal(2, set.All.Count);
        var file = set.Find("model-a", QuestionType.Choice, 4)!;
        Assert.Equal(Tau.Calibration.OptionBucket.ThreeToFive, file.Bucket);
        Assert.Equal("2026-09-27", file.Fitted.Date);
        Assert.Equal(repo.Manifest.Sha256, file.Fitted.DatasetRevision);
        Assert.Equal("demo", file.Fitted.Dataset);
        Assert.Equal(400, file.Fitted.N);
        Assert.True(summary.OfflineHeldOut!.Ece < summary.RawHeldOut!.Ece);

        // The offline view is exactly the shared calibrator applied to the raw held-out probabilities.
        var raw = WorkbenchJson.ReadJsonl<MeasuredItem>(repo.Spec.RunPath("model-a", "heldout", Phases.Raw));
        var offline = WorkbenchJson.ReadJsonl<MeasuredItem>(repo.Spec.RunPath("model-a", "heldout", Phases.Offline));
        var cal = new Calibrator(file);
        for (int i = 0; i < raw.Count; i++)
        {
            Assert.Equal(cal.ApplyToProbabilities(raw[i].Vector(repo.Spec.Question)!), offline[i].Vector(repo.Spec.Question));
        }

        Assert.True(File.Exists(repo.Spec.RunSummaryPath("model-a", "calibration", Phases.Offline)));
        Assert.True(File.Exists(repo.Spec.CalibrationSummaryPath("model-a")));
    }

    [Fact]
    public void RerunningCalibrateReplacesStaleCalibrators()
    {
        using var repo = TestRepo.Create(calibration: 400, heldOut: 50);
        Synthetic.WriteRawRuns(repo, "model-a", 2, seed: 2);
        var dir = repo.Spec.CalibratorsDirectory("model-a");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "old.calibrator.json"), "{}");
        CalibrateStage.Run(repo.Spec, "model-a", repo.Manifest);
        Assert.False(File.Exists(Path.Combine(dir, "old.calibrator.json")));
    }

    [Fact]
    public void SmallCalibrationSetsGetNoBucketCalibratorAndSayWhy()
    {
        using var repo = TestRepo.Create(calibration: 120, heldOut: 40);
        Synthetic.WriteRawRuns(repo, "model-a", 2, seed: 4);
        var summary = CalibrateStage.Run(repo.Spec, "model-a", repo.Manifest);
        var only = Assert.Single(summary.Calibrators);
        Assert.Equal("temperature", only.Chosen);
        Assert.NotNull(only.IsotonicSkipped);
        Assert.Contains(summary.Notes, n => n.Contains("No option-count bucket calibrator", StringComparison.Ordinal));
    }

    [Fact]
    public void ANonTauEndpointCannotBeCalibrated()
    {
        using var repo = TestRepo.Create(calibration: 50, heldOut: 10);
        Synthetic.WriteRawRuns(repo, "model-a", 2, seed: 1, tau: false);
        var e = Assert.Throws<WorkbenchException>(() => CalibrateStage.Run(repo.Spec, "model-a", repo.Manifest));
        Assert.Contains("model hash", e.Message, StringComparison.Ordinal);
        Assert.Contains("not a Tau endpoint", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CalibrateWithoutRawMeasurementsSaysWhatToRun()
    {
        using var repo = TestRepo.Create();
        var e = Assert.Throws<WorkbenchException>(() => CalibrateStage.Run(repo.Spec, "model-a", repo.Manifest));
        Assert.Contains("Run the stage that produces it first", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ClassesWithoutCalibrationExamplesAreListed()
    {
        using var repo = TestRepo.Create(calibration: 3, heldOut: 4, classes: 6);
        Synthetic.WriteRawRuns(repo, "model-a", 2, seed: 9);
        var summary = CalibrateStage.Run(repo.Spec, "model-a", repo.Manifest);
        Assert.Equal(["d", "e", "f"], summary.ClassesWithoutCalibrationExamples);
    }

    [Fact]
    public void NoulCalibratorsFitOnTheTwoWayVector()
    {
        using var repo = TestRepo.Create(type: "noul", calibration: 300, heldOut: 100);
        Synthetic.WriteRawRuns(repo, "model-a", 2.5, seed: 6);
        var summary = CalibrateStage.Run(repo.Spec, "model-a", repo.Manifest);
        Assert.Equal("2 options", summary.Calibrators[1].Scope);
        Assert.True(summary.OfflineHeldOut!.Ece <= summary.RawHeldOut!.Ece + 0.02);
    }
}
