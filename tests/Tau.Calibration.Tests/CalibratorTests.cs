using Tau.Calibration;

namespace Tau.Calibration.Tests;

/// <summary>
/// <see cref="Calibrator.ApplyToProbabilities"/>: format v1 calibrators act on the log of the model's
/// reference probabilities (research R-01). Expected values are worked by hand, not by calling the code under test.
/// </summary>
public class CalibratorTests
{
    private const string ModelHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private static CalibratorFitted Fitted() => new(100, "held-out", null, "2026-09-27", null, null, "tau-fit", "1.0.0");

    private static Calibrator Temperature(double t) =>
        new(CalibratorFile.CreateTemperature("m", ModelHash, QuestionType.Choice, null, t, Fitted()));

    private static Calibrator Isotonic(double[] x, double[] y) =>
        new(CalibratorFile.CreateIsotonic("m", ModelHash, QuestionType.Choice, null, new IsotonicKnots(x, y), Fitted()));

    [Fact]
    public void Temperature_two_takes_the_square_root_of_each_probability_then_renormalises()
    {
        // softmax(log p / 2) = sqrt(p) / sum(sqrt(p)).
        double[] actual = Temperature(2.0).ApplyToProbabilities([0.8, 0.2]);

        double a = Math.Sqrt(0.8), b = Math.Sqrt(0.2); // 0.894427191, 0.447213595
        Assert.Equal(a / (a + b), actual[0], precision: 12); // 2/3
        Assert.Equal(b / (a + b), actual[1], precision: 12); // 1/3
        Assert.Equal(2.0 / 3.0, actual[0], precision: 12);
    }

    [Fact]
    public void Temperature_half_squares_each_probability_then_renormalises()
    {
        // softmax(log p / 0.5) = p^2 / sum(p^2). p = [3, 2, 1] / 6, so p^2 ∝ [9, 4, 1] → [9, 4, 1] / 14.
        double[] actual = Temperature(0.5).ApplyToProbabilities([0.5, 1.0 / 3.0, 1.0 / 6.0]);

        Assert.Equal(9.0 / 14.0, actual[0], precision: 12);
        Assert.Equal(4.0 / 14.0, actual[1], precision: 12);
        Assert.Equal(1.0 / 14.0, actual[2], precision: 12);
    }

    [Fact]
    public void Temperature_one_returns_the_reference_probabilities()
    {
        double[] p = [0.7, 0.2, 0.1];
        double[] actual = Temperature(1.0).ApplyToProbabilities(p);

        for (int i = 0; i < p.Length; i++)
        {
            Assert.Equal(p[i], actual[i], precision: 12);
        }
    }

    [Fact]
    public void Temperature_absorbs_four_dp_rounding_by_renormalising()
    {
        // Rounded endpoint output that sums to 1.0001: T = 1 returns it renormalised.
        double[] actual = Temperature(1.0).ApplyToProbabilities([0.3334, 0.3334, 0.3333]);

        Assert.Equal(1.0, actual.Sum(), precision: 12);
        Assert.Equal(0.3334 / 1.0001, actual[0], precision: 12);
    }

    [Fact]
    public void Zero_probabilities_are_clamped_to_one_in_a_million_before_the_log()
    {
        // With T = 2: sqrt(1e-6) = 1e-3 and sqrt(1) = 1, so p' = [1, 1e-3] / 1.001.
        double[] actual = Temperature(2.0).ApplyToProbabilities([1.0, 0.0]);

        Assert.Equal(1.0 / 1.001, actual[0], precision: 12);
        Assert.Equal(0.001 / 1.001, actual[1], precision: 12);
        Assert.All(actual, p => Assert.True(double.IsFinite(p)));
    }

    [Fact]
    public void Values_below_the_floor_are_treated_exactly_like_the_floor()
    {
        double[] a = Temperature(3.0).ApplyToProbabilities([0.999999, 1e-9]);
        double[] b = Temperature(3.0).ApplyToProbabilities([0.999999, 1e-6]);

        Assert.Equal(b, a);
    }

    [Fact]
    public void LogReferenceProbabilities_is_the_clamped_natural_log()
    {
        double[] z = Calibrator.LogReferenceProbabilities([0.5, 0.0, 1e-7]);

        Assert.Equal(Math.Log(0.5), z[0], precision: 15);
        Assert.Equal(Math.Log(1e-6), z[1], precision: 15);
        Assert.Equal(Math.Log(1e-6), z[2], precision: 15);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Invalid_probabilities_are_rejected(double bad)
    {
        Assert.Throws<ArgumentException>(() => Temperature(2.0).ApplyToProbabilities([0.5, bad]));
    }

    [Fact]
    public void Empty_and_null_inputs_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => Temperature(2.0).ApplyToProbabilities([]));
        Assert.Throws<ArgumentNullException>(() => Temperature(2.0).ApplyToProbabilities(null!));
    }

    [Fact]
    public void Isotonic_identity_returns_the_reference_probabilities()
    {
        double[] p = [0.7, 0.2, 0.1];
        double[] actual = Isotonic([0.0, 0.5, 1.0], [0.0, 0.5, 1.0]).ApplyToProbabilities(p);

        for (int i = 0; i < p.Length; i++)
        {
            Assert.Equal(p[i], actual[i], precision: 12);
        }
    }

    [Fact]
    public void Isotonic_maps_each_probability_then_renormalises()
    {
        // Knots (0, 0.1), (0.5, 0.5), (1, 0.9): f(p) = 0.1 + 0.8p for p in [0, 1].
        // p = [0.8, 0.2]: f = [0.74, 0.26], which already sums to 1.
        // p = [0.6, 0.3, 0.1]: f = [0.58, 0.34, 0.18], sum 1.1 → [0.58, 0.34, 0.18] / 1.1.
        var cal = Isotonic([0.0, 0.5, 1.0], [0.1, 0.5, 0.9]);

        double[] two = cal.ApplyToProbabilities([0.8, 0.2]);
        Assert.Equal(0.74, two[0], precision: 12);
        Assert.Equal(0.26, two[1], precision: 12);

        double[] three = cal.ApplyToProbabilities([0.6, 0.3, 0.1]);
        Assert.Equal(0.58 / 1.1, three[0], precision: 12);
        Assert.Equal(0.34 / 1.1, three[1], precision: 12);
        Assert.Equal(0.18 / 1.1, three[2], precision: 12);
    }

    [Fact]
    public void Isotonic_applies_to_the_clamped_probabilities()
    {
        // p = [1, 0] clamps to [1, 1e-6], renormalised to [1/1.000001, 1e-6/1.000001] before the map.
        // With the identity map the output equals that renormalised vector.
        double[] actual = Isotonic([0.0, 1.0], [0.0, 1.0]).ApplyToProbabilities([1.0, 0.0]);

        Assert.Equal(1.0 / 1.000001, actual[0], precision: 12);
        Assert.Equal(1e-6 / 1.000001, actual[1], precision: 12);
    }

    [Fact]
    public void Isotonic_all_mapped_values_zero_falls_back_to_uniform()
    {
        double[] actual = Isotonic([0.0, 1.0], [0.0, 0.0]).ApplyToProbabilities([0.7, 0.2, 0.1]);

        Assert.All(actual, p => Assert.Equal(1.0 / 3.0, p, precision: 12));
    }

    [Fact]
    public void Option_order_does_not_change_the_result()
    {
        // Noul is [false, true] for Laya and [true, false] for Von: the same pair in either order must give
        // the same calibrated P(true).
        var t = Temperature(3.0);
        double[] forward = t.ApplyToProbabilities([0.1, 0.9]);
        double[] reversed = t.ApplyToProbabilities([0.9, 0.1]);
        Assert.Equal(forward[1], reversed[0], precision: 15);

        var iso = Isotonic([0.0, 0.5, 1.0], [0.1, 0.5, 0.9]);
        Assert.Equal(iso.ApplyToProbabilities([0.1, 0.9])[1], iso.ApplyToProbabilities([0.9, 0.1])[0], precision: 15);
    }
}
