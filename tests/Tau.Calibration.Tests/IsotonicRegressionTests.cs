using Tau.Calibration;

namespace Tau.Calibration.Tests;

public class IsotonicRegressionTests
{
    [Fact]
    public void Fit_KnownSequence_EqualWeights_PoolsAdjacentViolators()
    {
        // y = [1, 3, 2, 4] with equal weights -> [1, 2.5, 2.5, 4] (hand-computed PAV: 3 and 2 violate
        // monotonicity and pool to their average 2.5; verified with a reference Python PAV
        // implementation: py -3.12 pav([1,3,2,4],[1,1,1,1]) -> [1.0, 2.5, 2.5, 4.0]).
        double[] x = [1, 2, 3, 4];
        double[] y = [1, 3, 2, 4];

        var fit = IsotonicRegression.Fit(x, y);

        Assert.Equal([1.0, 2.5, 2.5, 4.0], fit.Y);
        Assert.Equal([1.0, 2.0, 3.0, 4.0], fit.X);
    }

    [Fact]
    public void Fit_WeightedSequence_PoolsUsingWeightedAverage()
    {
        // y = [1, 3, 2, 4], weights = [1, 2, 1, 1] -> [1, 2.6667, 2.6667, 4] (weighted average of
        // the pooled block: (3*2 + 2*1) / (2+1) = 8/3; verified with a reference Python PAV
        // implementation: py -3.12 pav([1,3,2,4],[1,2,1,1]) -> [1.0, 2.6666666666666665, 2.6666666666666665, 4.0]).
        double[] x = [1, 2, 3, 4];
        double[] y = [1, 3, 2, 4];
        double[] weights = [1, 2, 1, 1];

        var fit = IsotonicRegression.Fit(x, y, weights);

        Assert.Equal(1.0, fit.Y[0], precision: 12);
        Assert.Equal(8.0 / 3.0, fit.Y[1], precision: 12);
        Assert.Equal(8.0 / 3.0, fit.Y[2], precision: 12);
        Assert.Equal(4.0, fit.Y[3], precision: 12);
    }

    [Fact]
    public void Fit_MultiBlockWeightedSequence_MatchesReference()
    {
        // y = [1, 2, 0, 4, 3], weights = [2, 1, 1, 1, 3] -> [1, 1, 1, 3.25, 3.25] (verified with a
        // reference Python PAV implementation: py -3.12 pav([1,2,0,4,3],[2,1,1,1,3])
        // -> [1.0, 1.0, 1.0, 3.25, 3.25]).
        double[] x = [1, 2, 3, 4, 5];
        double[] y = [1, 2, 0, 4, 3];
        double[] weights = [2, 1, 1, 1, 3];

        var fit = IsotonicRegression.Fit(x, y, weights);

        Assert.Equal([1.0, 1.0, 1.0, 3.25, 3.25], fit.Y);
    }

    [Fact]
    public void Fit_AlreadyNonDecreasing_IsUnchanged()
    {
        double[] x = [0, 1, 2, 3];
        double[] y = [0.1, 0.4, 0.6, 0.9];

        var fit = IsotonicRegression.Fit(x, y);

        Assert.Equal(y, fit.Y);
    }

    [Fact]
    public void Fit_SortsUnsortedXBeforePooling()
    {
        double[] x = [3, 1, 2, 4];
        double[] y = [2, 1, 3, 4];

        var fit = IsotonicRegression.Fit(x, y);

        Assert.Equal([1.0, 2.0, 3.0, 4.0], fit.X);
        Assert.Equal([1.0, 2.5, 2.5, 4.0], fit.Y);
    }

    [Fact]
    public void Fit_MismatchedLengths_Throws()
    {
        Assert.Throws<ArgumentException>(() => IsotonicRegression.Fit([1.0, 2.0], [1.0]));
    }

    [Fact]
    public void Fit_EmptyInput_Throws()
    {
        Assert.Throws<ArgumentException>(() => IsotonicRegression.Fit([], []));
    }

    [Fact]
    public void Fit_NonPositiveWeight_Throws()
    {
        Assert.Throws<ArgumentException>(() => IsotonicRegression.Fit([1.0, 2.0], [0.1, 0.2], [1.0, 0.0]));
    }

    [Fact]
    public void Apply_InterpolatesLinearlyBetweenKnots()
    {
        var fit = IsotonicRegression.FromKnots([0.0, 0.5, 1.0], [0.0, 0.5, 1.0]);

        Assert.Equal(0.25, fit.Apply(0.25), precision: 12);
        Assert.Equal(0.75, fit.Apply(0.75), precision: 12);
        Assert.Equal(0.5, fit.Apply(0.5), precision: 12);
    }

    [Fact]
    public void Apply_ClampsBelowRange()
    {
        var fit = IsotonicRegression.FromKnots([0.2, 0.8], [0.3, 0.9]);

        Assert.Equal(0.3, fit.Apply(0.0), precision: 12);
        Assert.Equal(0.3, fit.Apply(-1.0), precision: 12);
    }

    [Fact]
    public void Apply_ClampsAboveRange()
    {
        var fit = IsotonicRegression.FromKnots([0.2, 0.8], [0.3, 0.9]);

        Assert.Equal(0.9, fit.Apply(1.0), precision: 12);
        Assert.Equal(0.9, fit.Apply(2.0), precision: 12);
    }

    [Fact]
    public void Apply_NonUniformKnotSpacing_InterpolatesCorrectly()
    {
        var fit = IsotonicRegression.FromKnots([0.0, 0.1, 0.9, 1.0], [0.0, 0.2, 0.8, 1.0]);

        // Between x=0.1 (y=0.2) and x=0.9 (y=0.8): at x=0.5, t=(0.5-0.1)/(0.9-0.1)=0.5
        double expected = 0.2 + (0.5 * (0.8 - 0.2));
        Assert.Equal(expected, fit.Apply(0.5), precision: 12);
    }

    [Fact]
    public void FromKnots_NonAscendingX_Throws()
    {
        Assert.Throws<ArgumentException>(() => IsotonicRegression.FromKnots([0.5, 0.2], [0.1, 0.2]));
    }

    [Fact]
    public void FromKnots_NonDecreasingYViolated_Throws()
    {
        Assert.Throws<ArgumentException>(() => IsotonicRegression.FromKnots([0.1, 0.2], [0.5, 0.2]));
    }

    [Fact]
    public void FromKnots_TooFewPoints_Throws()
    {
        Assert.Throws<ArgumentException>(() => IsotonicRegression.FromKnots([0.1], [0.5]));
    }
}
