using Tau.Contract;
using Tau.Inference.Models;

namespace Tau.Inference.PostProcessing;

/// <summary>
/// Turns Von logits into contract answers exactly as von-sdk 1.2.3's <c>OptionMarkerBackend</c> does:
/// an input-conditioned temperature (bias + entropy + state length + option count, clamped), a float32 softmax,
/// TypeSafe's margin confidence <c>(n·p_max − 1)/(n − 1)</c> rounded to 3 dp, the choice taken from the
/// UNSCALED logits, noul = p[0] with options ordered [true, false] and the zero-shot null-state prior
/// correction, and score = Σ i·pᵢ rounded to 2 dp.
/// </summary>
public sealed class VonPostProcessor
{
    private readonly VonPostProcessing _post;

    /// <summary>Creates a post-processor for the Von package.</summary>
    /// <param name="post">The package's reference post-processing parameters.</param>
    public VonPostProcessor(VonPostProcessing post) => _post = post;

    /// <summary>
    /// <c>_effective_temperature</c>: the fitted map applied to the request's own features, or the scalar
    /// temperature when no map ships. Monotonic, so it never changes the argmax.
    /// </summary>
    /// <param name="logits">Unscaled logits (after any noul prior correction).</param>
    /// <param name="stateTokens">Token count of the formatted state, without special tokens.</param>
    /// <param name="nOptions">Number of options (2 for noul).</param>
    public double EffectiveTemperature(ReadOnlySpan<float> logits, int stateTokens, int nOptions)
    {
        if (_post.CalibrationMap is not { } m) return _post.Temperature;
        var probs = Numerics.SoftmaxF32(logits);
        var n = Math.Max(probs.Length, 1);
        var ent = n > 1 ? Numerics.EntropyF32(probs) / Math.Log(n) : 0.0;
        var tokens = Math.Max(stateTokens, 1);
        // Same accumulation order as the reference: bias, entropy, log_tokens, n_options.
        double raw = 0;
        raw += m.Bias * 1.0;
        raw += m.Entropy * ent;
        raw += m.LogTokens * (Math.Log10(tokens) / 4.0);
        raw += m.NOptions * (nOptions / 8.0);
        return Math.Min(m.Hi, Math.Max(m.Lo, raw));
    }

    /// <summary>Choice answer.</summary>
    /// <param name="keys">Option keys in request order.</param>
    /// <param name="logits">One logit per option.</param>
    /// <param name="stateTokens">State token count (for the temperature map).</param>
    public ChoiceAnswer Choice(IReadOnlyList<string> keys, ReadOnlySpan<float> logits, int stateTokens)
    {
        var p = Probabilities(logits, stateTokens, keys.Count);
        return FromChoiceProbabilities(keys, p, Numerics.ArgMaxTie(logits));
    }

    /// <summary>Builds the choice answer from probabilities (reference or calibrated).</summary>
    /// <param name="keys">Option keys.</param>
    /// <param name="p">Probabilities.</param>
    /// <param name="argMax">Chosen index (the reference takes it from the unscaled logits).</param>
    public static ChoiceAnswer FromChoiceProbabilities(IReadOnlyList<string> keys, ReadOnlySpan<float> p, int argMax)
    {
        var probs = new OrderedDictionary<string, double>(keys.Count);
        for (var i = 0; i < keys.Count; i++) probs[keys[i]] = Numerics.PyRound(p[i], 4);
        return new ChoiceAnswer { Choice = keys[argMax], Probabilities = probs, Confidence = MarginConfidence(p) };
    }

    /// <summary>Score answer.</summary>
    /// <param name="legend">Level descriptions as Von renders them (index 0 first).</param>
    /// <param name="logits">One logit per level.</param>
    /// <param name="stateTokens">State token count.</param>
    public ScoreAnswer Score(IReadOnlyList<string> legend, ReadOnlySpan<float> logits, int stateTokens) =>
        FromScoreProbabilities(legend, Probabilities(logits, stateTokens, legend.Count));

    /// <summary>Builds the score answer from probabilities (reference or calibrated).</summary>
    /// <param name="legend">Level descriptions.</param>
    /// <param name="p">Probabilities.</param>
    public static ScoreAnswer FromScoreProbabilities(IReadOnlyList<string> legend, ReadOnlySpan<float> p)
    {
        var probs = new OrderedDictionary<string, double>(p.Length);
        var leg = new OrderedDictionary<string, string>(p.Length);
        double weighted = 0;
        for (var i = 0; i < p.Length; i++)
        {
            var key = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            probs[key] = Numerics.PyRound(p[i], 4);
            leg[key] = legend[i];
            weighted += i * (double)p[i];
        }
        return new ScoreAnswer
        {
            Score = Numerics.PyRound(weighted, 2), Legend = leg, Probabilities = probs, Confidence = MarginConfidence(p),
        };
    }

    /// <summary>
    /// Noul answer. <paramref name="logits"/> are [true, false]. When the question had no explicit criteria,
    /// <paramref name="nullLogits"/> are the same question scored against an empty state, and the reference's
    /// zero-shot prior correction is applied to the "true" logit.
    /// </summary>
    /// <param name="logits">[true, false] logits for the real state.</param>
    /// <param name="nullLogits">[true, false] logits for the empty state, or empty when criteria were explicit.</param>
    /// <param name="stateTokens">State token count.</param>
    public NoulAnswer Noul(ReadOnlySpan<float> logits, ReadOnlySpan<float> nullLogits, int stateTokens)
    {
        Span<float> z = [logits[0], logits[1]];
        if (!nullLogits.IsEmpty)
        {
            var bias = nullLogits[0] - nullLogits[1];
            var correction = _post.NoulPrior is { } np ? (float)np.A * bias + (float)np.B : 0.7f * bias;
            z[0] = logits[0] - correction;
        }
        var p = Probabilities(z, stateTokens, 2);
        return new NoulAnswer { Noul = Numerics.PyRound(Math.Clamp((double)p[0], 0.0, 1.0), 4) };
    }

    /// <summary>TypeSafe's margin confidence, rounded to 3 dp: (n·p_max − 1)/(n − 1), clamped; 1 when n ≤ 1.</summary>
    /// <param name="p">Probabilities.</param>
    public static double MarginConfidence(ReadOnlySpan<float> p)
    {
        var n = p.Length;
        if (n <= 1) return 1.0;
        double pMax = p[0];
        for (var i = 1; i < n; i++) if (p[i] > pMax) pMax = p[i];
        var c = (n * pMax - 1) / (n - 1);
        return Numerics.PyRound(Math.Clamp(c, 0.0, 1.0), 3);
    }

    private float[] Probabilities(ReadOnlySpan<float> logits, int stateTokens, int nOptions)
    {
        var t = Math.Max(EffectiveTemperature(logits, stateTokens, nOptions), 1e-4);
        return Numerics.SoftmaxF32(Numerics.Scale(logits, t));
    }
}
