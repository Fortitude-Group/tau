using Tau.Calibration;

namespace Tau.Calibration.Tests;

public class MetricsTests
{
    // ECE reference values below were cross-checked against a direct Python transliteration of the
    // laya.common.ece_score reference (laya 0.3.20):
    //
    //   edges = np.linspace(0, 1, bins + 1); e = 0.0
    //   for i, (lo, hi) in enumerate(zip(edges[:-1], edges[1:])):
    //       sel = (conf >= lo if i == 0 else conf > lo) & (conf <= hi)
    //       if sel.any(): e += sel.mean() * abs(conf[sel].mean() - correct[sel].mean())
    //
    // run via: py -3.12 <script using numpy> (numpy 1.26.4).

    [Fact]
    public void ExpectedCalibrationError_PerfectlyCalibrated_IsZero()
    {
        // py: ece_score([0.0,0.0,1.0,1.0],[0,0,1,1]) -> 0.0
        double[] conf = [0.0, 0.0, 1.0, 1.0];
        bool[] correct = [false, false, true, true];

        double ece = Metrics.ExpectedCalibrationError(conf, correct);

        Assert.Equal(0.0, ece, precision: 12);
    }

    [Fact]
    public void ExpectedCalibrationError_FirstBinIsInclusiveOfZero()
    {
        // py: ece_score([0.0],[1]) -> 1.0 (a lone conf==0.0 point falls in the first bin, which is
        // closed on both ends, so it is not silently dropped; avgConf=0, avgCorrect=1, weight=1).
        double[] conf = [0.0];
        bool[] correct = [true];

        double ece = Metrics.ExpectedCalibrationError(conf, correct);

        Assert.Equal(1.0, ece, precision: 12);
    }

    [Fact]
    public void ExpectedCalibrationError_TwoPointsInFirstBin()
    {
        // py: ece_score([0.0,0.05],[1,0]) -> 0.475
        double[] conf = [0.0, 0.05];
        bool[] correct = [true, false];

        double ece = Metrics.ExpectedCalibrationError(conf, correct);

        Assert.Equal(0.475, ece, precision: 12);
    }

    [Fact]
    public void ExpectedCalibrationError_ValueExactlyOnBinEdge_BelongsToLowerBin()
    {
        // 2/15 is the edge between bin 1 (0.0667, 0.1333] and bin 2 (0.1333, 0.2]. Since non-first
        // bins are right-closed only (conf > lo & conf <= hi), a value exactly equal to an edge
        // belongs to the bin it is the *upper* bound of, not the one it is the lower bound of.
        // py: ece_score([2.0/15],[0]) -> 0.13333333333333333 == 2/15
        double[] conf = [2.0 / 15];
        bool[] correct = [false];

        double ece = Metrics.ExpectedCalibrationError(conf, correct);

        Assert.Equal(2.0 / 15, ece, precision: 12);
    }

    [Fact]
    public void ExpectedCalibrationError_EmptyInput_IsNaN()
    {
        double ece = Metrics.ExpectedCalibrationError([], []);

        Assert.True(double.IsNaN(ece));
    }

    [Fact]
    public void ExpectedCalibrationError_MismatchedLengths_Throws()
    {
        Assert.Throws<ArgumentException>(() => Metrics.ExpectedCalibrationError([0.5], [true, false]));
    }

    [Fact]
    public void ExpectedCalibrationError_NonPositiveBins_Throws()
    {
        Assert.Throws<ArgumentException>(() => Metrics.ExpectedCalibrationError([0.5], [true], bins: 0));
    }

    [Fact]
    public void BrierScore_HandComputedExample()
    {
        // probs = [[0.7,0.2,0.1],[0.1,0.8,0.1]], labels=[0,1]
        // sample0: (0.7-1)^2+(0.2-0)^2+(0.1-0)^2 = 0.09+0.04+0.01 = 0.14
        // sample1: (0.1-0)^2+(0.8-1)^2+(0.1-0)^2 = 0.01+0.04+0.01 = 0.06
        // mean = (0.14+0.06)/2 = 0.10
        // py: brier(...) -> 0.10000000000000002
        double[][] probs = [[0.7, 0.2, 0.1], [0.1, 0.8, 0.1]];
        int[] labels = [0, 1];

        double brier = Metrics.BrierScore(probs, labels);

        Assert.Equal(0.10, brier, precision: 9);
    }

    [Fact]
    public void BrierScore_PerfectPrediction_IsZero()
    {
        double[][] probs = [[1.0, 0.0], [0.0, 1.0]];
        int[] labels = [0, 1];

        double brier = Metrics.BrierScore(probs, labels);

        Assert.Equal(0.0, brier, precision: 12);
    }

    [Fact]
    public void LogLoss_HandComputedExample()
    {
        // py: logloss(...) -> 0.2899092476264711 == mean(-ln(0.7), -ln(0.8))
        double[][] probs = [[0.7, 0.2, 0.1], [0.1, 0.8, 0.1]];
        int[] labels = [0, 1];

        double logLoss = Metrics.LogLoss(probs, labels);

        Assert.Equal(0.2899092476264711, logLoss, precision: 9);
    }

    [Fact]
    public void LogLoss_FloorsZeroProbability()
    {
        double[][] probs = [[0.0, 1.0]];
        int[] labels = [0];

        double logLoss = Metrics.LogLoss(probs, labels);

        Assert.Equal(-Math.Log(1e-12), logLoss, precision: 6);
    }

    [Fact]
    public void Accuracy_ComputesFractionCorrect()
    {
        double[][] probs = [[0.9, 0.1], [0.2, 0.8], [0.6, 0.4]];
        int[] labels = [0, 1, 1];

        double accuracy = Metrics.Accuracy(probs, labels);

        Assert.Equal(2.0 / 3.0, accuracy, precision: 12);
    }

    [Fact]
    public void Accuracy_TieBreaksToLowestIndex()
    {
        double[][] probs = [[0.5, 0.5]];
        int[] labels = [0];

        double accuracy = Metrics.Accuracy(probs, labels);

        Assert.Equal(1.0, accuracy, precision: 12);
    }

    [Fact]
    public void BrierScore_MismatchedLengths_Throws()
    {
        Assert.Throws<ArgumentException>(() => Metrics.BrierScore([[0.5, 0.5]], [0, 1]));
    }

    [Fact]
    public void LogLoss_LabelOutOfRange_Throws()
    {
        Assert.Throws<ArgumentException>(() => Metrics.LogLoss([[0.5, 0.5]], [5]));
    }

    [Fact]
    public void Accuracy_EmptyInput_Throws()
    {
        Assert.Throws<ArgumentException>(() => Metrics.Accuracy([], []));
    }
}
