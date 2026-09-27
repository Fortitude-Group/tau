using Tau.Calibration;

namespace Tau.Calibration.Tests;

public class CalibratorTests
{
    private const string ModelHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private static CalibratorFitted Fitted() => new(100, "held-out", null, "2026-09-27", null, null, "tau-fit", "1.0.0");

    [Fact]
    public void Apply_Temperature_EqualsSoftmaxOfScaledLogits()
    {
        var file = CalibratorFile.CreateTemperature("m", ModelHash, QuestionType.Choice, null, temperature: 2.0, Fitted());
        var calibrator = new Calibrator(file);

        double[] rawLogits = [2.0, 0.0];
        double[] expected = Softmax.ApplyTemperature(rawLogits, 2.0);
        double[] actual = calibrator.Apply(rawLogits);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Apply_Isotonic_MapsThenRenormalises()
    {
        var isotonic = new IsotonicKnots([0.0, 0.5, 1.0], [0.0, 0.5, 1.0]); // identity mapping
        var file = CalibratorFile.CreateIsotonic("m", ModelHash, QuestionType.Choice, null, isotonic, Fitted());
        var calibrator = new Calibrator(file);

        double[] rawLogits = [1.0, 0.0, -1.0];
        double[] softmaxProbs = Softmax.Compute(rawLogits);
        double[] actual = calibrator.Apply(rawLogits);

        // The isotonic function here is the identity, so mapped == softmax probs, which already
        // sum to 1: renormalisation should be a no-op.
        for (int i = 0; i < actual.Length; i++)
        {
            Assert.Equal(softmaxProbs[i], actual[i], precision: 9);
        }

        Assert.Equal(1.0, actual.Sum(), precision: 9);
    }

    [Fact]
    public void Apply_Isotonic_RenormalisesWhenMappedValuesDoNotSumToOne()
    {
        // A non-identity monotone map: everything gets compressed towards the top half of [0,1].
        var isotonic = new IsotonicKnots([0.0, 1.0], [0.5, 1.0]);
        var file = CalibratorFile.CreateIsotonic("m", ModelHash, QuestionType.Choice, null, isotonic, Fitted());
        var calibrator = new Calibrator(file);

        double[] rawLogits = [1.0, 0.0, -1.0];
        double[] actual = calibrator.Apply(rawLogits);

        Assert.Equal(1.0, actual.Sum(), precision: 9);
        Assert.All(actual, p => Assert.True(p >= 0));
    }

    [Fact]
    public void Apply_Isotonic_AllMappedValuesZero_FallsBackToUniform()
    {
        var isotonic = new IsotonicKnots([0.0, 1.0], [0.0, 0.0]);
        var file = CalibratorFile.CreateIsotonic("m", ModelHash, QuestionType.Choice, null, isotonic, Fitted());
        var calibrator = new Calibrator(file);

        double[] rawLogits = [3.0, 1.0, -2.0];
        double[] actual = calibrator.Apply(rawLogits);

        Assert.All(actual, p => Assert.Equal(1.0 / 3.0, p, precision: 12));
    }
}
