namespace Tau.Inference.PostProcessing;

/// <summary>
/// Numeric helpers that reproduce the reference runtimes' arithmetic. Both references compute the
/// softmax in float32 (NumPy 2 keeps float32 when a float32 array meets a Python float; torch does the
/// same), so the float32 helpers here are deliberate, not an optimisation.
/// </summary>
public static class Numerics
{
    /// <summary>Logits within this distance of the maximum count as tied (see DECISIONS "argmax tie-break").</summary>
    public const float TieEpsilon = 1e-4f;

    /// <summary>
    /// First index whose value is within <see cref="TieEpsilon"/> of the maximum: the reference's first-index
    /// argmax rule (torch.argmax / np.argmax), robust to float noise on exact ties such as duplicate options.
    /// </summary>
    /// <param name="values">Scores; must not be empty.</param>
    public static int ArgMaxTie(ReadOnlySpan<float> values)
    {
        if (values.IsEmpty) throw new ArgumentException("empty", nameof(values));
        var max = values[0];
        for (var i = 1; i < values.Length; i++)
            if (values[i] > max) max = values[i];
        for (var i = 0; i < values.Length; i++)
            if (values[i] >= max - TieEpsilon) return i;
        return 0;
    }

    /// <summary>Numerically stable softmax in float32: <c>e = exp(z - max(z)); e / sum(e)</c>.</summary>
    /// <param name="logits">Logits.</param>
    public static float[] SoftmaxF32(ReadOnlySpan<float> logits)
    {
        var max = float.NegativeInfinity;
        foreach (var z in logits) if (z > max) max = z;
        var p = new float[logits.Length];
        var sum = 0f;
        for (var i = 0; i < p.Length; i++)
        {
            p[i] = MathF.Exp(logits[i] - max);
            sum += p[i];
        }
        for (var i = 0; i < p.Length; i++) p[i] /= sum;
        return p;
    }

    /// <summary>Divides every logit by <paramref name="temperature"/> in float32.</summary>
    /// <param name="logits">Logits.</param>
    /// <param name="temperature">Temperature (&gt; 0).</param>
    public static float[] Scale(ReadOnlySpan<float> logits, double temperature)
    {
        var t = (float)temperature;
        var z = new float[logits.Length];
        for (var i = 0; i < z.Length; i++) z[i] = logits[i] / t;
        return z;
    }

    /// <summary>Python's <c>round(x, digits)</c>: round half to even.</summary>
    /// <param name="x">Value.</param>
    /// <param name="digits">Decimal places.</param>
    public static double PyRound(double x, int digits) => Math.Round(x, digits, MidpointRounding.ToEven);

    /// <summary>Normalised Shannon entropy of <paramref name="p"/> in float32, divided by ln(n) in double.</summary>
    /// <param name="p">Probabilities.</param>
    public static float EntropyF32(ReadOnlySpan<float> p)
    {
        var ent = 0f;
        foreach (var v in p) ent -= v * MathF.Log(Math.Clamp(v, 1e-12f, 1f));
        return ent;
    }
}
