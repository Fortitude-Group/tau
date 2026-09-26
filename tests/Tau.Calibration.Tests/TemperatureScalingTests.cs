using Tau.Calibration;

namespace Tau.Calibration.Tests;

public class TemperatureScalingTests
{
    private static (double[][] Logits, int[] Labels) GenerateSyntheticData(int seed, double trueTemperature, int n, int classes)
    {
        var rng = new Random(seed);
        var logits = new double[n][];
        var labels = new int[n];

        for (int i = 0; i < n; i++)
        {
            var l = new double[classes];
            for (int c = 0; c < classes; c++)
            {
                l[c] = (rng.NextDouble() * 6.0) - 3.0; // Uniform(-3, 3)
            }

            logits[i] = l;

            double[] probs = Softmax.ApplyTemperature(l, trueTemperature);
            double u = rng.NextDouble();
            double cumulative = 0.0;
            int label = classes - 1;
            for (int c = 0; c < classes; c++)
            {
                cumulative += probs[c];
                if (u <= cumulative)
                {
                    label = c;
                    break;
                }
            }

            labels[i] = label;
        }

        return (logits, labels);
    }

    [Fact]
    public void Fit_RecoversKnownTemperature_T2()
    {
        (double[][] logits, int[] labels) = GenerateSyntheticData(seed: 12345, trueTemperature: 2.0, n: 5000, classes: 5);

        double fitted = TemperatureScaling.Fit(logits, labels);

        Assert.True(Math.Abs(fitted - 2.0) <= 0.1, $"Expected fitted T within 0.1 of 2.0, got {fitted}.");
    }

    [Fact]
    public void Fit_AlreadyCalibratedData_RecoversApproximatelyOne()
    {
        (double[][] logits, int[] labels) = GenerateSyntheticData(seed: 54321, trueTemperature: 1.0, n: 5000, classes: 5);

        double fitted = TemperatureScaling.Fit(logits, labels);

        Assert.True(Math.Abs(fitted - 1.0) <= 0.1, $"Expected fitted T within 0.1 of 1.0, got {fitted}.");
    }

    [Fact]
    public void Fit_IsDeterministic()
    {
        (double[][] logits, int[] labels) = GenerateSyntheticData(seed: 999, trueTemperature: 1.5, n: 500, classes: 3);

        double first = TemperatureScaling.Fit(logits, labels);
        double second = TemperatureScaling.Fit(logits, labels);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Fit_SupportsVariableOptionCountsPerSample()
    {
        double[][] logits =
        [
            [1.0, 0.0],
            [1.0, 0.0, 0.0],
            [2.0, 0.0, 0.0, 0.0, 0.0],
        ];
        int[] labels = [0, 0, 0];

        double fitted = TemperatureScaling.Fit(logits, labels);

        Assert.True(fitted > 0);
    }

    [Fact]
    public void Fit_MismatchedLengths_Throws()
    {
        double[][] logits = [[1.0, 2.0]];
        int[] labels = [0, 1];

        Assert.Throws<ArgumentException>(() => TemperatureScaling.Fit(logits, labels));
    }

    [Fact]
    public void Fit_EmptyInput_Throws()
    {
        Assert.Throws<ArgumentException>(() => TemperatureScaling.Fit([], []));
    }

    [Fact]
    public void Fit_LabelOutOfRange_Throws()
    {
        double[][] logits = [[1.0, 2.0]];
        int[] labels = [5];

        Assert.Throws<ArgumentException>(() => TemperatureScaling.Fit(logits, labels));
    }
}
