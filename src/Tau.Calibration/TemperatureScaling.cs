namespace Tau.Calibration;

/// <summary>
/// Fits a single temperature parameter <c>T</c> that minimises the mean negative log-likelihood
/// of the observed labels under <c>softmax(logits / T)</c>.
/// </summary>
public static class TemperatureScaling
{
    /// <summary>The lower bound of the search interval, in temperature space (not log space).</summary>
    private const double MinTemperature = 0.05;

    /// <summary>The upper bound of the search interval, in temperature space (not log space).</summary>
    private const double MaxTemperature = 20.0;

    /// <summary>Tolerance (in log-T space) at which the golden-section search stops.</summary>
    private const double LogTolerance = 1e-6;

    /// <summary>Floor applied to a predicted probability before taking its logarithm, to avoid -infinity.</summary>
    private const double ProbabilityFloor = 1e-12;

    /// <summary>
    /// Fits the temperature <c>T</c> minimising the mean negative log-likelihood of
    /// <paramref name="labels"/> under <c>softmax(logits[i] / T)</c>, for each sample <c>i</c>.
    /// </summary>
    /// <param name="logits">
    /// Per-sample logits. Samples may have a different number of options; each
    /// <c>logits[i]</c> is the full logit vector for sample <c>i</c>. For a <c>tau.calibrator</c> v1
    /// temperature these are <see cref="Calibrator.LogReferenceProbabilities"/> of each sample's
    /// reference probabilities, which is what <see cref="Calibrator.ApplyToProbabilities"/> divides by
    /// <c>T</c>.
    /// </param>
    /// <param name="labels">Per-sample index of the correct option within <c>logits[i]</c>.</param>
    /// <returns>
    /// The fitted temperature, found by golden-section search over <c>log T</c> in
    /// <c>[ln 0.05, ln 20]</c>, deterministic to a tolerance of 1e-6 in log-T space.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="logits"/> and <paramref name="labels"/> differ in length, are
    /// empty, or a label index is out of range for its sample's logits.
    /// </exception>
    public static double Fit(IReadOnlyList<double[]> logits, IReadOnlyList<int> labels)
    {
        ArgumentNullException.ThrowIfNull(logits);
        ArgumentNullException.ThrowIfNull(labels);
        if (logits.Count != labels.Count)
        {
            throw new ArgumentException("Logits and labels must have the same number of samples.", nameof(labels));
        }

        if (logits.Count == 0)
        {
            throw new ArgumentException("At least one sample is required to fit a temperature.", nameof(logits));
        }

        for (int i = 0; i < logits.Count; i++)
        {
            if (labels[i] < 0 || labels[i] >= logits[i].Length)
            {
                throw new ArgumentException($"Label {labels[i]} at sample {i} is out of range for {logits[i].Length} option(s).", nameof(labels));
            }
        }

        double MeanNegativeLogLikelihood(double logT)
        {
            double temperature = Math.Exp(logT);
            double total = 0.0;
            for (int i = 0; i < logits.Count; i++)
            {
                var probs = Softmax.ApplyTemperature(logits[i], temperature);
                total += -Math.Log(Math.Max(probs[labels[i]], ProbabilityFloor));
            }

            return total / logits.Count;
        }

        double logLo = Math.Log(MinTemperature);
        double logHi = Math.Log(MaxTemperature);
        double bestLogT = GoldenSectionMinimize(MeanNegativeLogLikelihood, logLo, logHi, LogTolerance);
        return Math.Exp(bestLogT);
    }

    /// <summary>
    /// Minimises a unimodal function <paramref name="f"/> over <c>[lo, hi]</c> using golden-section
    /// search, a deterministic, derivative-free method that halves the search interval by a fixed
    /// ratio (the golden ratio) on every iteration.
    /// </summary>
    private static double GoldenSectionMinimize(Func<double, double> f, double lo, double hi, double tolerance)
    {
        const double InverseGoldenRatio = 0.6180339887498949; // (sqrt(5) - 1) / 2

        double a = lo;
        double b = hi;
        double c = b - (InverseGoldenRatio * (b - a));
        double d = a + (InverseGoldenRatio * (b - a));
        double fc = f(c);
        double fd = f(d);

        while (Math.Abs(b - a) > tolerance)
        {
            if (fc < fd)
            {
                b = d;
                d = c;
                fd = fc;
                c = b - (InverseGoldenRatio * (b - a));
                fc = f(c);
            }
            else
            {
                a = c;
                c = d;
                fc = fd;
                d = a + (InverseGoldenRatio * (b - a));
                fd = f(d);
            }
        }

        return (a + b) / 2.0;
    }
}
