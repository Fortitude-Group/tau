using System.Reflection;
using Tau.Calibration;

namespace Tau.Workbench.Calibrate;

/// <summary>One method's fit on the calibration split, and how well it did there.</summary>
/// <param name="Method">temperature or isotonic.</param>
/// <param name="Temperature">The fitted temperature (temperature only).</param>
/// <param name="Knots">Knot count after collapsing ties (isotonic only).</param>
/// <param name="CalibrationEce">ECE on the calibration split after applying the fit.</param>
/// <param name="CalibrationLogLoss">Log loss on the calibration split after applying the fit.</param>
public sealed record MethodFit(string Method, double? Temperature, int? Knots, double CalibrationEce, double CalibrationLogLoss);

/// <summary>The outcome of fitting one calibrator: both methods' scores, the chosen file, and why.</summary>
/// <param name="File">The chosen calibrator.</param>
/// <param name="Temperature">The temperature fit.</param>
/// <param name="Isotonic">The isotonic fit, or null when it was not attempted.</param>
/// <param name="IsotonicSkipped">Why isotonic was not attempted, when it wasn't.</param>
/// <param name="EceBefore">Calibration-split ECE before calibration.</param>
/// <param name="LogLossBefore">Calibration-split log loss before calibration.</param>
public sealed record FitOutcome(CalibratorFile File, MethodFit Temperature, MethodFit? Isotonic, string? IsotonicSkipped, double EceBefore, double LogLossBefore);

/// <summary>
/// Fits temperature and isotonic calibrators on reference probabilities (research R-01) and keeps the
/// one with the lower calibration-split log loss. All fitting and scoring is Tau.Calibration's
/// (<see cref="TemperatureScaling"/>, <see cref="IsotonicRegression"/>, <see cref="Metrics"/>); this
/// class only prepares the inputs and chooses.
/// </summary>
public static class CalibrationFitter
{
    /// <summary>The fewest items isotonic regression is fitted on; below it, temperature only.</summary>
    public const int MinIsotonicItems = 200;

    /// <summary>The probability floor before taking logs (research R-01).</summary>
    public const double ProbabilityFloor = 1e-6;

    /// <summary>The tool name written into calibrator provenance.</summary>
    public const string ToolName = "tau-workbench";

    /// <summary>This assembly's informational version, for calibrator provenance.</summary>
    public static string ToolVersion { get; } =
        typeof(CalibrationFitter).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "0.0.0";

    /// <summary>Fits both methods and returns the one with lower calibration-split log loss.</summary>
    /// <param name="probabilities">Reference probability vectors from the calibration split.</param>
    /// <param name="gold">Gold class indices.</param>
    /// <param name="model">The model id the calibrator is for.</param>
    /// <param name="modelHash">The model's ONNX sha256.</param>
    /// <param name="questionType">The question type.</param>
    /// <param name="bucket">The option-count bucket, or null for a type-level calibrator.</param>
    /// <param name="dataset">Provenance: the dataset name.</param>
    /// <param name="datasetRevision">Provenance: the dataset revision.</param>
    /// <param name="date">Provenance: the fitting date (yyyy-MM-dd).</param>
    /// <exception cref="ArgumentException">No items were given.</exception>
    public static FitOutcome Fit(
        IReadOnlyList<double[]> probabilities, IReadOnlyList<int> gold, string model, string modelHash,
        QuestionType questionType, OptionBucket? bucket, string dataset, string? datasetRevision, string date)
    {
        ArgumentNullException.ThrowIfNull(probabilities);
        ArgumentNullException.ThrowIfNull(gold);
        if (probabilities.Count == 0)
        {
            throw new ArgumentException("A calibrator needs at least one item.", nameof(probabilities));
        }

        int n = probabilities.Count;
        var (eceBefore, logLossBefore) = Score(probabilities, gold);
        CalibratorFitted Provenance(double eceAfter) => new(n, dataset, datasetRevision, date, eceBefore, eceAfter, ToolName, ToolVersion);

        var logits = probabilities.Select(p => p.Select(x => Math.Log(Math.Max(x, ProbabilityFloor))).ToArray()).ToArray();
        double t = TemperatureScaling.Fit(logits, gold);
        var tempFile = CalibratorFile.CreateTemperature(model, modelHash, questionType, bucket, t, Provenance(0));
        var (tempEce, tempLoss) = Score(Apply(tempFile, probabilities), gold);
        var tempFit = new MethodFit("temperature", t, null, tempEce, tempLoss);

        CalibratorFile chosen = CalibratorFile.CreateTemperature(model, modelHash, questionType, bucket, t, Provenance(tempEce));
        MethodFit? isoFit = null;
        string? skipped = null;
        if (n < MinIsotonicItems)
        {
            skipped = $"only {n} calibration items (isotonic needs at least {MinIsotonicItems}), so temperature scaling was used.";
        }
        else
        {
            var confidence = probabilities.Select(p => p.Max()).ToArray();
            var correct = probabilities.Select((p, i) => ArgMax(p) == gold[i] ? 1.0 : 0.0).ToArray();
            if (confidence.Distinct().Count() < 2)
            {
                skipped = "every calibration item had the same confidence, so no isotonic curve could be fitted; temperature scaling was used.";
            }
            else
            {
                var knots = CollapseKnots(IsotonicRegression.Fit(confidence, correct));
                var isoFile =CalibratorFile.CreateIsotonic(model, modelHash, questionType, bucket, knots, Provenance(0));
                var (isoEce, isoLoss) = Score(Apply(isoFile, probabilities), gold);
                isoFit = new MethodFit("isotonic", null, knots.X.Count, isoEce, isoLoss);
                if (isoLoss < tempLoss)
                {
                    chosen = CalibratorFile.CreateIsotonic(model, modelHash, questionType, bucket, knots, Provenance(isoEce));
                }
            }
        }

        return new FitOutcome(chosen, tempFit, isoFit, skipped, eceBefore, logLossBefore);
    }

    /// <summary>
    /// Collapses a PAV fit (one knot per input point, ties in x allowed) into knots with strictly
    /// ascending x, as the calibrator file requires: points sharing an x are averaged into one knot.
    /// Because PAV output is non-decreasing in sorted order, averages of consecutive runs stay
    /// non-decreasing, so the result is still monotone.
    /// <para>
    /// When the lowest confidence is above 0, an anchor knot (0, 0) is prepended. The fit is on max(p),
    /// but the Runtime maps every option's probability through the curve (then renormalises); without the
    /// anchor, every low-probability option would be lifted to the curve's first y value, which flattens
    /// a many-option distribution. With it, a probability near 0 stays near 0.
    /// </para>
    /// </summary>
    /// <param name="fit">A fitted isotonic regression.</param>
    public static IsotonicKnots CollapseKnots(IsotonicRegression fit)
    {
        ArgumentNullException.ThrowIfNull(fit);
        var xs = new List<double>();
        var ys = new List<double>();
        if (fit.X.Count > 0 && fit.X[0] > 0)
        {
            xs.Add(0);
            ys.Add(0);
        }

        int i = 0;
        while (i < fit.X.Count)
        {
            double x = fit.X[i];
            double sum = 0;
            int count = 0;
            while (i < fit.X.Count && fit.X[i] == x)
            {
                sum += fit.Y[i];
                count++;
                i++;
            }

            double y = Math.Clamp(sum / count, 0, 1);
            if (ys.Count > 0 && y < ys[^1])
            {
                y = ys[^1]; // guards floating-point rounding only; PAV output is already monotone
            }

            xs.Add(Math.Clamp(x, 0, 1));
            ys.Add(y);
        }

        return new IsotonicKnots(xs, ys);
    }

    /// <summary>Applies a calibrator to every vector (through the shared Tau.Calibration path).</summary>
    /// <param name="file">The calibrator.</param>
    /// <param name="probabilities">Reference probability vectors.</param>
    public static double[][] Apply(CalibratorFile file, IReadOnlyList<double[]> probabilities)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(probabilities);
        var calibrator = new Calibrator(file);
        return probabilities.Select(p => calibrator.ApplyToProbabilities(p)).ToArray();
    }

    private static (double Ece, double LogLoss) Score(IReadOnlyList<double[]> probabilities, IReadOnlyList<int> gold)
    {
        var confidence = probabilities.Select(p => p.Max()).ToArray();
        var correct = probabilities.Select((p, i) => ArgMax(p) == gold[i]).ToArray();
        return (Metrics.ExpectedCalibrationError(confidence, correct, 15), Metrics.LogLoss(probabilities, gold));
    }

    private static int ArgMax(double[] p) => Measure.MetricSet.ArgMax(p);
}
