namespace Tau.Calibration;

/// <summary>
/// Calibration and classification quality metrics: expected calibration error, Brier score,
/// log loss, and accuracy.
/// </summary>
public static class Metrics
{
    /// <summary>Floor applied to a predicted probability before taking its logarithm, to avoid -infinity.</summary>
    private const double ProbabilityFloor = 1e-12;

    /// <summary>
    /// Computes the expected calibration error (ECE), reproducing exactly the Laya reference
    /// (<c>laya.common.ece_score</c>, laya 0.3.20):
    /// <code>
    /// edges = np.linspace(0, 1, bins + 1); e = 0.0
    /// for i, (lo, hi) in enumerate(zip(edges[:-1], edges[1:])):
    ///     sel = (conf >= lo if i == 0 else conf > lo) &amp; (conf &lt;= hi)
    ///     if sel.any(): e += sel.mean() * abs(conf[sel].mean() - correct[sel].mean())
    /// </code>
    /// Bins are right-closed (<c>(lo, hi]</c>) except the first, which is closed on both ends
    /// (<c>[0, hi]</c>) so a confidence of exactly 0 is counted. Each non-empty bin contributes its
    /// share of the total sample count (<c>count / n</c>) times the absolute gap between the bin's
    /// mean confidence and its mean accuracy (fraction correct).
    /// </summary>
    /// <param name="confidence">Per-sample predicted confidence, each expected in <c>[0, 1]</c>.</param>
    /// <param name="correct">Per-sample correctness of the prediction the confidence was for.</param>
    /// <param name="bins">Number of equal-width bins spanning <c>[0, 1]</c>. Defaults to 15.</param>
    /// <returns>The expected calibration error, or <see cref="double.NaN"/> when the input is empty.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="confidence"/> and <paramref name="correct"/> differ in length,
    /// or <paramref name="bins"/> is not positive.
    /// </exception>
    public static double ExpectedCalibrationError(IReadOnlyList<double> confidence, IReadOnlyList<bool> correct, int bins = 15)
    {
        ArgumentNullException.ThrowIfNull(confidence);
        ArgumentNullException.ThrowIfNull(correct);
        if (confidence.Count != correct.Count)
        {
            throw new ArgumentException("confidence and correct must have the same length.", nameof(correct));
        }

        if (bins <= 0)
        {
            throw new ArgumentException("bins must be positive.", nameof(bins));
        }

        int n = confidence.Count;
        if (n == 0)
        {
            return double.NaN;
        }

        double step = 1.0 / bins;
        double e = 0.0;
        for (int i = 0; i < bins; i++)
        {
            double lo = i * step;
            double hi = (i + 1) * step;

            int count = 0;
            double sumConf = 0.0;
            double sumCorrect = 0.0;
            for (int j = 0; j < n; j++)
            {
                double c = confidence[j];
                bool inBin = i == 0 ? c >= lo && c <= hi : c > lo && c <= hi;
                if (!inBin)
                {
                    continue;
                }

                count++;
                sumConf += c;
                sumCorrect += correct[j] ? 1.0 : 0.0;
            }

            if (count == 0)
            {
                continue;
            }

            double weight = (double)count / n;
            double avgConf = sumConf / count;
            double avgCorrect = sumCorrect / count;
            e += weight * Math.Abs(avgConf - avgCorrect);
        }

        return e;
    }

    /// <summary>
    /// Computes the mean Brier score: for each sample, the sum over classes of the squared
    /// difference between the predicted probability and the one-hot label, averaged over samples.
    /// </summary>
    /// <param name="probs">Per-sample predicted probability vectors (need not be pre-normalised).</param>
    /// <param name="labels">Per-sample correct class index into the corresponding <c>probs[i]</c>.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="probs"/> and <paramref name="labels"/> differ in length, are
    /// empty, or a label index is out of range for its sample.
    /// </exception>
    public static double BrierScore(IReadOnlyList<double[]> probs, IReadOnlyList<int> labels)
    {
        ValidateProbsAndLabels(probs, labels);

        double total = 0.0;
        for (int i = 0; i < probs.Count; i++)
        {
            var p = probs[i];
            int label = labels[i];
            double sample = 0.0;
            for (int k = 0; k < p.Length; k++)
            {
                double oneHot = k == label ? 1.0 : 0.0;
                double diff = p[k] - oneHot;
                sample += diff * diff;
            }

            total += sample;
        }

        return total / probs.Count;
    }

    /// <summary>
    /// Computes the mean log loss: for each sample, <c>-ln(max(p_label, 1e-12))</c>, averaged over
    /// samples.
    /// </summary>
    /// <param name="probs">Per-sample predicted probability vectors.</param>
    /// <param name="labels">Per-sample correct class index into the corresponding <c>probs[i]</c>.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="probs"/> and <paramref name="labels"/> differ in length, are
    /// empty, or a label index is out of range for its sample.
    /// </exception>
    public static double LogLoss(IReadOnlyList<double[]> probs, IReadOnlyList<int> labels)
    {
        ValidateProbsAndLabels(probs, labels);

        double total = 0.0;
        for (int i = 0; i < probs.Count; i++)
        {
            double p = probs[i][labels[i]];
            total += -Math.Log(Math.Max(p, ProbabilityFloor));
        }

        return total / probs.Count;
    }

    /// <summary>
    /// Computes the fraction of samples where the argmax of <c>probs[i]</c> equals
    /// <c>labels[i]</c>. Ties are broken by the lowest index, matching <see cref="Array.IndexOf{T}(T[], T)"/>-style argmax.
    /// </summary>
    /// <param name="probs">Per-sample predicted probability vectors.</param>
    /// <param name="labels">Per-sample correct class index into the corresponding <c>probs[i]</c>.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="probs"/> and <paramref name="labels"/> differ in length, are
    /// empty, or a label index is out of range for its sample.
    /// </exception>
    public static double Accuracy(IReadOnlyList<double[]> probs, IReadOnlyList<int> labels)
    {
        ValidateProbsAndLabels(probs, labels);

        int correct = 0;
        for (int i = 0; i < probs.Count; i++)
        {
            var p = probs[i];
            int argmax = 0;
            double best = p[0];
            for (int k = 1; k < p.Length; k++)
            {
                if (p[k] > best)
                {
                    best = p[k];
                    argmax = k;
                }
            }

            if (argmax == labels[i])
            {
                correct++;
            }
        }

        return (double)correct / probs.Count;
    }

    private static void ValidateProbsAndLabels(IReadOnlyList<double[]> probs, IReadOnlyList<int> labels)
    {
        ArgumentNullException.ThrowIfNull(probs);
        ArgumentNullException.ThrowIfNull(labels);
        if (probs.Count != labels.Count)
        {
            throw new ArgumentException("probs and labels must have the same length.", nameof(labels));
        }

        if (probs.Count == 0)
        {
            throw new ArgumentException("At least one sample is required.", nameof(probs));
        }

        for (int i = 0; i < probs.Count; i++)
        {
            if (labels[i] < 0 || labels[i] >= probs[i].Length)
            {
                throw new ArgumentException($"Label {labels[i]} at sample {i} is out of range for {probs[i].Length} option(s).", nameof(labels));
            }
        }
    }
}
