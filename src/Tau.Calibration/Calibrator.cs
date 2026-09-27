namespace Tau.Calibration;

/// <summary>
/// Applies a single fitted <see cref="CalibratorFile"/> to a model's reference probabilities, producing
/// calibrated probabilities.
/// </summary>
/// <remarks>
/// <para>
/// <b>Format v1 semantics.</b> A v1 calibrator's input is the log of the model's <i>reference
/// probabilities</i>: the exact distribution a <c>/v1/systemone</c> endpoint returns for an uncalibrated
/// answer (the Tau Runtime's <c>x-tau-raw: true</c> output, before its 4-dp rounding). It is not the
/// model's raw logits. This is what lets a calibrator fitted by the Workbench, which only ever sees
/// probabilities from an endpoint, mean exactly the same thing when the Runtime applies it
/// (research R-01, Principle XV).
/// </para>
/// <para>
/// Fitting follows the same rule: <see cref="TemperatureScaling.Fit"/> is fitted on
/// <see cref="LogReferenceProbabilities"/> of each item's reference probabilities, and
/// <see cref="IsotonicRegression"/> on the (clamped, renormalised) reference probabilities themselves.
/// </para>
/// </remarks>
public sealed class Calibrator
{
    /// <summary>
    /// The floor every reference probability is clamped to before its log is taken, so a probability
    /// the endpoint rounded to 0 still has a finite log.
    /// </summary>
    public const double ProbabilityFloor = 1e-6;

    /// <summary>Wraps a validated <see cref="CalibratorFile"/> so it can be applied to reference probabilities.</summary>
    /// <param name="file">The calibrator definition.</param>
    public Calibrator(CalibratorFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        File = file;
    }

    /// <summary>The underlying calibrator definition.</summary>
    public CalibratorFile File { get; }

    /// <summary>
    /// The calibrator input for one item: <c>log(max(p_i, 1e-6))</c> for each option's reference
    /// probability. Use it when fitting a temperature so the fit and the application see the same values.
    /// </summary>
    /// <param name="referenceProbabilities">The model's reference probabilities, one per option.</param>
    /// <returns>The clamped log probabilities, one per option.</returns>
    /// <exception cref="ArgumentException">
    /// The vector is empty, or holds a negative, NaN or infinite value.
    /// </exception>
    public static double[] LogReferenceProbabilities(IReadOnlyList<double> referenceProbabilities)
    {
        ArgumentNullException.ThrowIfNull(referenceProbabilities);
        if (referenceProbabilities.Count == 0)
        {
            throw new ArgumentException("At least one probability is required.", nameof(referenceProbabilities));
        }

        var z = new double[referenceProbabilities.Count];
        for (int i = 0; i < z.Length; i++)
        {
            double p = referenceProbabilities[i];
            if (!double.IsFinite(p) || p < 0)
            {
                throw new ArgumentException(
                    $"Probability {i} is {p}; reference probabilities must be finite and non-negative.",
                    nameof(referenceProbabilities));
            }

            z[i] = Math.Log(Math.Max(p, ProbabilityFloor));
        }

        return z;
    }

    /// <summary>
    /// Applies this calibrator to a model's reference probabilities (one per option) and returns
    /// calibrated probabilities summing to 1.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every probability is first clamped to <see cref="ProbabilityFloor"/> and logged
    /// (<see cref="LogReferenceProbabilities"/>); call the result <c>z</c>.
    /// </para>
    /// <para>
    /// For <see cref="CalibrationMethod.Temperature"/>: <c>softmax(z / T)</c>, i.e.
    /// <c>p'_i ∝ max(p_i, 1e-6)^(1/T)</c>.
    /// </para>
    /// <para>
    /// For <see cref="CalibrationMethod.Isotonic"/>: <c>softmax(z)</c> (the clamped probabilities,
    /// renormalised) is mapped option by option through the fitted isotonic function, and the mapped
    /// values are renormalised to sum to 1. The isotonic function is fitted and applied per option, not
    /// jointly, so the mapped values need not already sum to 1. If every mapped value is exactly 0 this
    /// returns a uniform distribution rather than dividing by zero.
    /// </para>
    /// <para>
    /// The vector's option order does not change the result for either method (both treat options
    /// symmetrically), but callers should pass the order the model reports so the output lines up.
    /// </para>
    /// </remarks>
    /// <param name="referenceProbabilities">
    /// The model's reference probabilities, one per option, as an uncalibrated (<c>x-tau-raw</c>) answer
    /// reports them. They should sum to 1; small departures (such as 4-dp rounding) are absorbed by the
    /// softmax.
    /// </param>
    /// <returns>Calibrated probabilities, one per option, summing to 1.</returns>
    /// <exception cref="ArgumentException">
    /// The vector is empty, or holds a negative, NaN or infinite value.
    /// </exception>
    public double[] ApplyToProbabilities(double[] referenceProbabilities)
    {
        var z = LogReferenceProbabilities(referenceProbabilities);

        if (File.Method == CalibrationMethod.Temperature)
        {
            return Softmax.ApplyTemperature(z, File.Temperature!.Value);
        }

        var probs = Softmax.Compute(z);
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
