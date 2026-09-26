namespace Tau.Calibration;

/// <summary>
/// Numerically stable softmax helpers operating in double precision.
/// </summary>
public static class Softmax
{
    /// <summary>
    /// Computes the softmax of <paramref name="logits"/>, i.e. <c>exp(x_i) / sum_j exp(x_j)</c>,
    /// using the standard max-subtraction trick for numerical stability.
    /// </summary>
    /// <param name="logits">Raw logits. Must contain at least one element.</param>
    /// <returns>A probability vector of the same length as <paramref name="logits"/>, summing to 1.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="logits"/> is empty.</exception>
    public static double[] Compute(IReadOnlyList<double> logits)
    {
        ArgumentNullException.ThrowIfNull(logits);
        if (logits.Count == 0)
        {
            throw new ArgumentException("Logits must contain at least one element.", nameof(logits));
        }

        double max = double.NegativeInfinity;
        for (int i = 0; i < logits.Count; i++)
        {
            if (logits[i] > max)
            {
                max = logits[i];
            }
        }

        var result = new double[logits.Count];
        double sum = 0.0;
        for (int i = 0; i < logits.Count; i++)
        {
            double e = Math.Exp(logits[i] - max);
            result[i] = e;
            sum += e;
        }

        for (int i = 0; i < result.Length; i++)
        {
            result[i] /= sum;
        }

        return result;
    }

    /// <summary>
    /// Applies temperature scaling to <paramref name="logits"/> and returns the resulting
    /// softmax probabilities: <c>softmax(logits / temperature)</c>.
    /// </summary>
    /// <param name="logits">Raw logits. Must contain at least one element.</param>
    /// <param name="temperature">The scaling temperature. Must be strictly positive.</param>
    /// <returns>A probability vector of the same length as <paramref name="logits"/>, summing to 1.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="temperature"/> is not positive.</exception>
    public static double[] ApplyTemperature(IReadOnlyList<double> logits, double temperature)
    {
        ArgumentNullException.ThrowIfNull(logits);
        if (temperature <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(temperature), temperature, "Temperature must be strictly positive.");
        }

        var scaled = new double[logits.Count];
        for (int i = 0; i < logits.Count; i++)
        {
            scaled[i] = logits[i] / temperature;
        }

        return Compute(scaled);
    }
}
