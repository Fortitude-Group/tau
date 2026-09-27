using Tau.Contract;
using Tau.Inference.Models;

namespace Tau.Inference.PostProcessing;

/// <summary>
/// Turns Laya logits into contract answers exactly as <c>laya.Agent._decode_answers</c> (laya 0.3.20) does:
/// the clamped per-bucket temperature, a float32 softmax, confidence = 1 − normalised entropy for choice and
/// score, the expected level for score (float64), P(true) = p[1] for noul, everything rounded to 4 dp.
/// Laya's non-contract fields (<c>answer_confidence</c>, <c>action</c>, noul <c>confidence</c>) are not produced.
/// </summary>
public sealed class LayaPostProcessor
{
    private readonly LayaPostProcessing _post;

    /// <summary>Creates a post-processor for one Laya package.</summary>
    /// <param name="post">The package's reference post-processing parameters.</param>
    public LayaPostProcessor(LayaPostProcessing post) => _post = post;

    /// <summary>The reference temperature for a question type and option count.</summary>
    /// <param name="type">choice, score or noul.</param>
    /// <param name="k">Number of options.</param>
    public double TemperatureFor(string type, int k)
    {
        var qt = type switch { "choice" => 0, "score" => 1, "noul" => 2, _ => throw new ArgumentException(type) };
        return _post.TemperatureByOptions.TryGetValue($"{type}:{Bucket(k)}", out var t) ? t : _post.Temperature[qt];
    }

    /// <summary>Laya's option-count bucket: 2, 3-5, 6-10 or 11+.</summary>
    /// <param name="k">Number of options.</param>
    public static string Bucket(int k) => k <= 2 ? "2" : k <= 5 ? "3-5" : k <= 10 ? "6-10" : "11+";

    /// <summary>Reference post-processing of one question's logits.</summary>
    /// <param name="type">choice, score or noul.</param>
    /// <param name="labels">Choice keys in order, or score legend texts in order; ignored for noul.</param>
    /// <param name="logits">The question's k option logits (padding slots already removed).</param>
    public Answer Decode(string type, IReadOnlyList<string> labels, ReadOnlySpan<float> logits)
    {
        var p = Numerics.SoftmaxF32(Numerics.Scale(logits, TemperatureFor(type, logits.Length)));
        return FromProbabilities(type, labels, p, Numerics.ArgMaxTie(logits));
    }

    /// <summary>
    /// Builds the contract answer from probabilities (the reference's output, or a Tau calibrator's).
    /// </summary>
    /// <param name="type">choice, score or noul.</param>
    /// <param name="labels">Choice keys or score legend texts, in order.</param>
    /// <param name="p">Probabilities over the k options (noul: [false, true]).</param>
    /// <param name="argMax">Index of the chosen option.</param>
    public Answer FromProbabilities(string type, IReadOnlyList<string> labels, ReadOnlySpan<float> p, int argMax)
    {
        var k = p.Length;
        switch (type)
        {
            case "choice":
            {
                var probs = new OrderedDictionary<string, double>(k);
                for (var i = 0; i < k; i++) probs[labels[i]] = Round(p[i]);
                return new ChoiceAnswer { Choice = labels[argMax], Probabilities = probs, Confidence = Round(EntropyConfidence(p)) };
            }
            case "score":
            {
                double expected = 0;
                for (var i = 0; i < k; i++) expected += i * (double)p[i];  // int64 × float32 → float64 in NumPy
                var probs = new OrderedDictionary<string, double>(k);
                var legend = new OrderedDictionary<string, string>(k);
                for (var i = 0; i < k; i++)
                {
                    probs[i.ToString(System.Globalization.CultureInfo.InvariantCulture)] = Round(p[i]);
                    legend[i.ToString(System.Globalization.CultureInfo.InvariantCulture)] = labels[i];
                }
                return new ScoreAnswer { Score = Round(expected), Legend = legend, Probabilities = probs, Confidence = Round(EntropyConfidence(p)) };
            }
            case "noul":
                return new NoulAnswer { Noul = Round(p[1]) };
            default:
                throw new ArgumentException($"unknown question type '{type}'", nameof(type));
        }
    }

    /// <summary><c>confidence_from_probs</c>: clip(1 − H(p)/ln k, 0, 1) in float32; 1 when k &lt; 2.</summary>
    /// <param name="p">Probabilities.</param>
    public static float EntropyConfidence(ReadOnlySpan<float> p)
    {
        var k = p.Length;
        if (k < 2) return 1f;
        var c = 1f - (float)(Numerics.EntropyF32(p) / Math.Log(k));
        return Math.Clamp(c, 0f, 1f);
    }

    private double Round(double x) => Numerics.PyRound(x, _post.Rounding);
}
