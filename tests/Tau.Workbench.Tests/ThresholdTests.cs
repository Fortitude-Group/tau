using Tau.Workbench.Measure;
using Tau.Workbench.Threshold;

namespace Tau.Workbench.Tests;

public sealed class ThresholdTests
{
    // Ten calibration items: confidence 0.95..0.50; the two least confident are wrong, and so is the 0.80 one.
    private static readonly double[] CalConf = [0.95, 0.90, 0.85, 0.80, 0.75, 0.70, 0.65, 0.60, 0.55, 0.50];
    private static readonly bool[] CalCorrect = [true, true, true, false, true, true, true, true, false, false];

    [Fact]
    public void PicksTheSmallestTauMeetingTheTargetOnCalibrationAndReportsHeldOut()
    {
        // Error at τ: 0.50 → 3/10; 0.51..0.55 → 2/9; 0.56..0.60 → 1/8 = 12.5%; ... 0.81..0.85 → 0/3.
        var heldConf = new[] { 0.99, 0.70, 0.62, 0.40 };
        var heldCorrect = new[] { true, false, true, true };
        var r = ThresholdStage.Choose("m", 0.125, "offline", CalConf, CalCorrect, heldConf, heldCorrect);
        Assert.True(r.Reachable);
        Assert.Equal(0.56, r.Tau!.Value, 10);
        Assert.Equal(0.8, r.CalibrationAcceptRate!.Value, 10);
        Assert.Equal(0.875, r.CalibrationAcceptedAccuracy!.Value, 10);
        Assert.Equal(3, r.HeldOutAccepted);
        Assert.Equal(0.75, r.HeldOutAcceptRate!.Value, 10);
        Assert.Equal(2 / 3.0, r.HeldOutAcceptedAccuracy!.Value, 10);
        Assert.Equal(101, r.CalibrationCurve.Count);
        Assert.Equal(101, r.HeldOutCurve.Count);
        Assert.Equal("offline", r.Source);
        Assert.Contains("τ = 0.56", r.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void AnErrorExactlyAtTheTargetCounts()
    {
        // At τ = 0.51, 9 items are kept and 2 are wrong: error 2/9 exactly.
        var r = ThresholdStage.Choose("m", 2 / 9.0, "raw", CalConf, CalCorrect, CalConf, CalCorrect);
        Assert.Equal(0.51, r.Tau!.Value, 10);
    }

    [Fact]
    public void AnUnreachableTargetIsReportedWithTheBestAchievableError()
    {
        var conf = new[] { 0.9, 0.8, 0.7 };
        var correct = new[] { false, true, false };
        var r = ThresholdStage.Choose("m", 0.05, "raw", conf, correct, conf, correct);
        Assert.False(r.Reachable);
        Assert.Null(r.Tau);
        Assert.Null(r.HeldOutAcceptRate);
        Assert.Equal(0.5, r.BestAchievableError!.Value, 10); // τ in (0.70, 0.80]: keeps 0.9 and 0.8, one wrong
        Assert.Equal(0.71, r.BestAchievableTau!.Value, 10);
        Assert.Contains("No threshold meets", r.Note, StringComparison.Ordinal);
        Assert.Contains("No threshold is invented", r.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void NoItemsMeansNoThreshold()
    {
        var r = ThresholdStage.Choose("m", 0.1, "raw", [], [], [], []);
        Assert.False(r.Reachable);
        Assert.Null(r.BestAchievableError);
        Assert.Contains("no measured items", r.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void CurvePointsCountAcceptedItems()
    {
        var p = ThresholdStage.At(0.7, CalConf, CalCorrect);
        Assert.Equal(6, p.Accepted);
        Assert.Equal(0.6, p.AcceptRate, 10);
        Assert.Equal(5 / 6.0, p.AcceptedAccuracy!.Value, 10);
        var none = ThresholdStage.At(1.0, CalConf, CalCorrect);
        Assert.Equal(0, none.Accepted);
        Assert.Null(none.AcceptedAccuracy);
        Assert.Equal(0.0, ThresholdStage.Grid[0]);
        Assert.Equal(1.0, ThresholdStage.Grid[^1]);
        Assert.Equal(0.57, ThresholdStage.Grid[57]);
    }

    [Fact]
    public void RunPrefersTheRuntimeCalibratedPhaseThenOfflineThenRaw()
    {
        using var repo = TestRepo.Create(calibration: 60, heldOut: 40);
        Synthetic.WriteRawRuns(repo, "model-a", 2, seed: 3);
        Assert.Equal(Phases.Raw, ThresholdStage.BestSource(repo.Spec, "model-a"));
        Calibrate.CalibrateStage.Run(repo.Spec, "model-a", repo.Manifest);
        Assert.Equal(Phases.Offline, ThresholdStage.BestSource(repo.Spec, "model-a"));
        var results = ThresholdStage.Run(repo.Spec);
        Assert.Equal("offline", Assert.Single(results).Source);
        Assert.True(File.Exists(repo.Spec.ThresholdPath));
    }

    [Fact]
    public void RunWithNothingMeasuredSaysSo()
    {
        using var repo = TestRepo.Create();
        Assert.Contains("tau measure", Assert.Throws<WorkbenchException>(() => ThresholdStage.Run(repo.Spec)).Message, StringComparison.Ordinal);
    }
}
