namespace Tau.Calibration;

/// <summary>
/// Applies a single fitted <see cref="CalibratorFile"/> to raw model logits, producing calibrated
/// probabilities.
/// </summary>
public sealed class Calibrator
{
    /// <summary>Wraps a validated <see cref="CalibratorFile"/> so it can be applied to logits.</summary>
    public Calibrator(CalibratorFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        File = file;
    }

    /// <summary>The underlying calibrator definition.</summary>
    public CalibratorFile File { get; }

    /// <summary>
    /// Applies this calibrator to a set of raw logits (one per option) and returns calibrated
    /// probabilities summing to 1.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For <see cref="CalibrationMethod.Temperature"/>, this is simply
    /// <c>softmax(rawLogits / T)</c>.
    /// </para>
    /// <para>
    /// For <see cref="CalibrationMethod.Isotonic"/>, <c>softmax(rawLogits)</c> is computed first,
    /// then each option's probability is mapped independently through the fitted isotonic
    /// function, and the mapped values are renormalised to sum to 1. Because the isotonic
    /// function is fitted (and applied) per-option rather than jointly, the mapped values are not
    /// guaranteed to already sum to 1, so renormalisation is required to produce a valid
    /// probability distribution. If every mapped value is exactly 0 (renormalisation would divide
    /// by zero), this falls back to a uniform distribution over the options rather than
    /// propagating a NaN.
    /// </para>
    /// </remarks>
    /// <param name="rawLogits">Raw model logits, one per option.</param>
    /// <returns>Calibrated probabilities, one per option, summing to 1.</returns>
    public double[] Apply(double[] rawLogits)
    {
        ArgumentNullException.ThrowIfNull(rawLogits);

        if (File.Method == CalibrationMethod.Temperature)
        {
            return Softmax.ApplyTemperature(rawLogits, File.Temperature!.Value);
        }

        var probs = Softmax.Compute(rawLogits);
        var isotonic = IsotonicRegression.FromKnots(File.Isotonic!.X, File.Isotonic.Y);
        var mapped = new double[probs.Length];
        double sum = 0.0;
        for (int i = 0; i < probs.Length; i++)
        {
            mapped[i] = isotonic.Apply(probs[i]);
            sum += mapped[i];
        }

        if (sum <= 0)
        {
            double uniform = 1.0 / mapped.Length;
            Array.Fill(mapped, uniform);
            return mapped;
        }

        for (int i = 0; i < mapped.Length; i++)
        {
            mapped[i] /= sum;
        }

        return mapped;
    }
}
