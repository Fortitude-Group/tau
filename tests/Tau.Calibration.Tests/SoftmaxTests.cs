using Tau.Calibration;

namespace Tau.Calibration.Tests;

public class SoftmaxTests
{
    [Fact]
    public void Compute_SumsToOne()
    {
        double[] logits = [1.0, 2.0, 3.0, -1.0];
        double[] probs = Softmax.Compute(logits);

        Assert.Equal(4, probs.Length);
        Assert.Equal(1.0, probs.Sum(), precision: 12);
        foreach (double p in probs)
        {
            Assert.True(p > 0);
        }
    }

    [Fact]
    public void Compute_UniformLogits_UniformProbabilities()
    {
        double[] logits = [5.0, 5.0, 5.0];
        double[] probs = Softmax.Compute(logits);

        Assert.All(probs, p => Assert.Equal(1.0 / 3.0, p, precision: 12));
    }

    [Fact]
    public void Compute_IsShiftInvariant()
    {
        double[] a = [1.0, 2.0, 3.0];
        double[] b = [1001.0, 1002.0, 1003.0];

        double[] pa = Softmax.Compute(a);
        double[] pb = Softmax.Compute(b);

        for (int i = 0; i < pa.Length; i++)
        {
            Assert.Equal(pa[i], pb[i], precision: 9);
        }
    }

    [Fact]
    public void Compute_HandlesLargeMagnitudeWithoutOverflow()
    {
        double[] logits = [1000.0, 999.0, 998.0];
        double[] probs = Softmax.Compute(logits);

        Assert.All(probs, p => Assert.False(double.IsNaN(p) || double.IsInfinity(p)));
        Assert.Equal(1.0, probs.Sum(), precision: 9);
    }

    [Fact]
    public void Compute_EmptyLogits_Throws()
    {
        Assert.Throws<ArgumentException>(() => Softmax.Compute([]));
    }

    [Fact]
    public void ApplyTemperature_OneEqualsPlainSoftmax()
    {
        double[] logits = [0.5, -1.0, 2.0];
        double[] direct = Softmax.Compute(logits);
        double[] viaTemperature = Softmax.ApplyTemperature(logits, 1.0);

        for (int i = 0; i < direct.Length; i++)
        {
            Assert.Equal(direct[i], viaTemperature[i], precision: 12);
        }
    }

    [Fact]
    public void ApplyTemperature_HighTemperature_FlattensDistribution()
    {
        double[] logits = [10.0, 0.0, -10.0];
        double[] probs = Softmax.ApplyTemperature(logits, 1000.0);

        Assert.All(probs, p => Assert.True(Math.Abs(p - (1.0 / 3.0)) < 0.01));
    }

    [Fact]
    public void ApplyTemperature_LowTemperature_SharpensDistribution()
    {
        double[] logits = [2.0, 1.0, 0.0];
        double[] probs = Softmax.ApplyTemperature(logits, 0.01);

        Assert.True(probs[0] > 0.999);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void ApplyTemperature_NonPositiveTemperature_Throws(double temperature)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Softmax.ApplyTemperature([1.0, 2.0], temperature));
    }
}
