using Tau.Calibration;

namespace Tau.Workbench.Measure;

/// <summary>One bin of a reliability diagram (15 equal bins, first bin closed on both ends, as the ECE).</summary>
/// <param name="Lower">Bin lower edge.</param>
/// <param name="Upper">Bin upper edge.</param>
/// <param name="Count">Items whose confidence falls in the bin.</param>
/// <param name="MeanConfidence">Mean max(p) of those items, or null when empty.</param>
/// <param name="Accuracy">Fraction of those items answered correctly, or null when empty.</param>
public sealed record ReliabilityBin(double Lower, double Upper, int Count, double? MeanConfidence, double? Accuracy);

/// <summary>
/// The quality metrics for one set of answers. Accuracy, ECE, Brier and log loss come from
/// <see cref="Metrics"/> in Tau.Calibration (constitution XV); nothing here re-derives them.
/// </summary>
public sealed record MetricSet
{
    /// <summary>The number of ECE bins (research R-07).</summary>
    public const int EceBins = 15;

    /// <summary>Why ECE is computed on max(p), stated in every summary and report (FR-009).</summary>
    public const string ConfidenceNote =
        "Confidence for ECE and thresholds is max(p), the probability of the chosen answer, not the contract's confidence field, which for choice and score questions is not a calibrated probability.";

    /// <summary>Items the metrics cover.</summary>
    public required int N { get; init; }

    /// <summary>Fraction where argmax(p) equals gold.</summary>
    public required double Accuracy { get; init; }

    /// <summary>Expected calibration error on max(p), 15 bins.</summary>
    public required double Ece { get; init; }

    /// <summary>Multi-class Brier score.</summary>
    public required double Brier { get; init; }

    /// <summary>Mean −ln p(gold), floored at 1e-12.</summary>
    public required double LogLoss { get; init; }

    /// <summary>Score questions only: mean absolute error in levels of argmax(p) against gold.</summary>
    public double? Mae { get; init; }

    /// <summary>The reliability diagram behind the ECE.</summary>
    public required IReadOnlyList<ReliabilityBin> Reliability { get; init; }

    /// <summary>Computes every metric.</summary>
    /// <param name="probabilities">Per-item probability vectors in class order.</param>
    /// <param name="gold">Per-item gold class index.</param>
    /// <param name="ordinal">True for score questions (adds MAE in levels).</param>
    /// <returns>The metrics, or null when there are no items.</returns>
    public static MetricSet? Compute(IReadOnlyList<double[]> probabilities, IReadOnlyList<int> gold, bool ordinal)
    {
        ArgumentNullException.ThrowIfNull(probabilities);
        ArgumentNullException.ThrowIfNull(gold);
        if (probabilities.Count == 0)
        {
            return null;
        }

        var confidence = probabilities.Select(p => p.Max()).ToArray();
        var predicted = probabilities.Select(ArgMax).ToArray();
        var correct = predicted.Select((p, i) => p == gold[i]).ToArray();
        return new MetricSet
        {
            N = probabilities.Count,
            Accuracy = Metrics.Accuracy(probabilities, gold),
            Ece = Metrics.ExpectedCalibrationError(confidence, correct, EceBins),
            Brier = Metrics.BrierScore(probabilities, gold),
            LogLoss = Metrics.LogLoss(probabilities, gold),
            Mae = ordinal ? predicted.Select((p, i) => (double)Math.Abs(p - gold[i])).Average() : null,
            Reliability = Bins(confidence, correct),
        };
    }

    /// <summary>Index of the largest probability; ties go to the lowest index, as <see cref="Metrics.Accuracy"/>.</summary>
    /// <param name="p">A probability vector.</param>
    public static int ArgMax(double[] p)
    {
        ArgumentNullException.ThrowIfNull(p);
        int best = 0;
        for (int k = 1; k < p.Length; k++)
        {
            if (p[k] > p[best])
            {
                best = k;
            }
        }

        return best;
    }

    /// <summary>
    /// Reliability bins with the same edges and membership rule as the ECE in Tau.Calibration, so the
    /// diagram shows exactly the gaps the ECE sums. (A test checks the weighted gaps add up to the ECE.)
    /// </summary>
    /// <param name="confidence">Per-item max(p).</param>
    /// <param name="correct">Per-item correctness.</param>
    public static IReadOnlyList<ReliabilityBin> Bins(IReadOnlyList<double> confidence, IReadOnlyList<bool> correct)
    {
        ArgumentNullException.ThrowIfNull(confidence);
        ArgumentNullException.ThrowIfNull(correct);
        var bins = new List<ReliabilityBin>(EceBins);
        double step = 1.0 / EceBins;
        for (int b = 0; b < EceBins; b++)
        {
            double lo = b * step, hi = (b + 1) * step;
            int count = 0;
            double sumConf = 0, sumCorrect = 0;
            for (int j = 0; j < confidence.Count; j++)
            {
                double c = confidence[j];
                if (b == 0 ? c >= lo && c <= hi : c > lo && c <= hi)
                {
                    count++;
                    sumConf += c;
                    sumCorrect += correct[j] ? 1 : 0;
                }
            }

            bins.Add(new ReliabilityBin(lo, hi, count, count == 0 ? null : sumConf / count, count == 0 ? null : sumCorrect / count));
        }

        return bins;
    }

    /// <summary>A linearly interpolated percentile (the R-7 definition), or null for no values.</summary>
    /// <param name="values">The values.</param>
    /// <param name="percentile">0 to 100.</param>
    public static double? Percentile(IEnumerable<double> values, double percentile)
    {
        ArgumentNullException.ThrowIfNull(values);
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0)
        {
            return null;
        }

        double rank = percentile / 100.0 * (sorted.Length - 1);
        int lo = (int)Math.Floor(rank);
        int hi = (int)Math.Ceiling(rank);
        return sorted[lo] + ((rank - lo) * (sorted[hi] - sorted[lo]));
    }
}
