using Tau.Workbench.Measure;
using Tau.Workbench.Spec;

namespace Tau.Workbench.Threshold;

/// <summary>One point of the accept-rate / accuracy trade-off curve.</summary>
/// <param name="Tau">The threshold: items with confidence ≥ τ are accepted locally.</param>
/// <param name="Accepted">Items accepted.</param>
/// <param name="AcceptRate">Accepted / all items.</param>
/// <param name="AcceptedAccuracy">Accuracy on accepted items, or null when none is accepted.</param>
public sealed record CurvePoint(double Tau, int Accepted, double AcceptRate, double? AcceptedAccuracy);

/// <summary>The threshold chosen for one model, and the evidence for it.</summary>
public sealed record ThresholdResult
{
    /// <summary>The model id.</summary>
    public required string Model { get; init; }

    /// <summary>The target error rate on accepted items.</summary>
    public required double TargetError { get; init; }

    /// <summary>Which measurement the confidences came from (calibrated, offline or raw).</summary>
    public required string Source { get; init; }

    /// <summary>Whether some τ on the calibration split meets the target.</summary>
    public required bool Reachable { get; init; }

    /// <summary>The chosen τ (null when unreachable).</summary>
    public double? Tau { get; init; }

    /// <summary>Calibration-split accept rate at τ.</summary>
    public double? CalibrationAcceptRate { get; init; }

    /// <summary>Calibration-split accepted accuracy at τ.</summary>
    public double? CalibrationAcceptedAccuracy { get; init; }

    /// <summary>Held-out accept rate at τ (the number to judge τ by).</summary>
    public double? HeldOutAcceptRate { get; init; }

    /// <summary>Held-out accepted accuracy at τ.</summary>
    public double? HeldOutAcceptedAccuracy { get; init; }

    /// <summary>Held-out items accepted at τ.</summary>
    public int? HeldOutAccepted { get; init; }

    /// <summary>Held-out items considered.</summary>
    public required int HeldOutItems { get; init; }

    /// <summary>When unreachable: the lowest calibration-split error any τ achieves (with ≥ 1 item accepted).</summary>
    public double? BestAchievableError { get; init; }

    /// <summary>When unreachable: the τ that achieves it.</summary>
    public double? BestAchievableTau { get; init; }

    /// <summary>The curve on the calibration split.</summary>
    public required IReadOnlyList<CurvePoint> CalibrationCurve { get; init; }

    /// <summary>The curve on the held-out split.</summary>
    public required IReadOnlyList<CurvePoint> HeldOutCurve { get; init; }

    /// <summary>A plain-English statement of the result.</summary>
    public required string Note { get; init; }
}

/// <summary>
/// The threshold stage (T025, research R-07): τ is chosen on the calibration split's calibrated
/// confidences as the smallest grid value (0.00 to 1.00 by 0.01) whose accepted-item error is at most
/// the target, then judged on the held-out split, so τ is never tuned on the numbers it is judged by.
/// </summary>
public static class ThresholdStage
{
    /// <summary>The τ grid: 0.00 to 1.00 in steps of 0.01.</summary>
    public static IReadOnlyList<double> Grid { get; } = Enumerable.Range(0, 101).Select(i => i / 100.0).ToArray();

    /// <summary>The trade-off curve over <see cref="Grid"/>.</summary>
    /// <param name="confidence">Per-item max(p).</param>
    /// <param name="correct">Per-item correctness.</param>
    public static IReadOnlyList<CurvePoint> Curve(IReadOnlyList<double> confidence, IReadOnlyList<bool> correct)
    {
        ArgumentNullException.ThrowIfNull(confidence);
        ArgumentNullException.ThrowIfNull(correct);
        return Grid.Select(t => At(t, confidence, correct)).ToArray();
    }

    /// <summary>The curve point at one τ.</summary>
    /// <param name="tau">The threshold.</param>
    /// <param name="confidence">Per-item max(p).</param>
    /// <param name="correct">Per-item correctness.</param>
    public static CurvePoint At(double tau, IReadOnlyList<double> confidence, IReadOnlyList<bool> correct)
    {
        ArgumentNullException.ThrowIfNull(confidence);
        ArgumentNullException.ThrowIfNull(correct);
        int accepted = 0, right = 0;
        for (int i = 0; i < confidence.Count; i++)
        {
            if (confidence[i] >= tau)
            {
                accepted++;
                right += correct[i] ? 1 : 0;
            }
        }

        return new CurvePoint(tau, accepted, confidence.Count == 0 ? 0 : (double)accepted / confidence.Count, accepted == 0 ? null : (double)right / accepted);
    }

    /// <summary>Chooses τ on the calibration split and reports it on the held-out split.</summary>
    /// <param name="model">The model id.</param>
    /// <param name="targetError">The target error on accepted items.</param>
    /// <param name="source">Which measurement the confidences came from.</param>
    /// <param name="calConfidence">Calibration-split max(p).</param>
    /// <param name="calCorrect">Calibration-split correctness.</param>
    /// <param name="heldConfidence">Held-out max(p).</param>
    /// <param name="heldCorrect">Held-out correctness.</param>
    public static ThresholdResult Choose(
        string model, double targetError, string source,
        IReadOnlyList<double> calConfidence, IReadOnlyList<bool> calCorrect,
        IReadOnlyList<double> heldConfidence, IReadOnlyList<bool> heldCorrect)
    {
        var calCurve = Curve(calConfidence, calCorrect);
        var heldCurve = Curve(heldConfidence, heldCorrect);
        // Compare with a small tolerance so an error of exactly the target (e.g. 1 - 0.95) is not lost to rounding.
        var chosen = calCurve.FirstOrDefault(p => p.AcceptedAccuracy is { } a && 1 - a <= targetError + 1e-12);
        if (chosen is null)
        {
            var best = calCurve.Where(p => p.AcceptedAccuracy is not null).OrderBy(p => 1 - p.AcceptedAccuracy!.Value).ThenBy(p => p.Tau).FirstOrDefault();
            return new ThresholdResult
            {
                Model = model,
                TargetError = targetError,
                Source = source,
                Reachable = false,
                HeldOutItems = heldConfidence.Count,
                BestAchievableError = best is null ? null : 1 - best.AcceptedAccuracy!.Value,
                BestAchievableTau = best?.Tau,
                CalibrationCurve = calCurve,
                HeldOutCurve = heldCurve,
                Note = best is null
                    ? "No threshold can be chosen: the calibration split has no measured items."
                    : $"No threshold meets the {Fmt.Pct(targetError)} target error on the calibration split. The lowest error any threshold reaches is {Fmt.Pct(1 - best.AcceptedAccuracy!.Value)}, at τ = {Fmt.Num(best.Tau, "0.00")}, accepting {Fmt.Pct(best.AcceptRate)} of items. No threshold is invented.",
            };
        }

        var held = At(chosen.Tau, heldConfidence, heldCorrect);
        return new ThresholdResult
        {
            Model = model,
            TargetError = targetError,
            Source = source,
            Reachable = true,
            Tau = chosen.Tau,
            CalibrationAcceptRate = chosen.AcceptRate,
            CalibrationAcceptedAccuracy = chosen.AcceptedAccuracy,
            HeldOutAcceptRate = held.AcceptRate,
            HeldOutAcceptedAccuracy = held.AcceptedAccuracy,
            HeldOutAccepted = held.Accepted,
            HeldOutItems = heldConfidence.Count,
            CalibrationCurve = calCurve,
            HeldOutCurve = heldCurve,
            Note = held.Accepted == 0
                ? $"τ = {Fmt.Num(chosen.Tau, "0.00")} meets the target on the calibration split but accepts no held-out item."
                : $"τ = {Fmt.Num(chosen.Tau, "0.00")} is the smallest threshold whose calibration-split error is at most {Fmt.Pct(targetError)}. On held-out it accepts {Fmt.Pct(held.AcceptRate)} of items with {Fmt.Pct(held.AcceptedAccuracy)} accuracy.",
        };
    }

    /// <summary>
    /// Picks the best available measurement of a split for thresholds and cascades: the Runtime's
    /// calibrated phase, else the Workbench's offline calibration, else raw (uncalibrated).
    /// </summary>
    /// <param name="spec">The decision spec.</param>
    /// <param name="model">The model id.</param>
    /// <returns>The phase name, or null when nothing has been measured.</returns>
    public static string? BestSource(DecisionSpec spec, string model)
    {
        ArgumentNullException.ThrowIfNull(spec);
        foreach (var phase in new[] { Phases.Calibrated, Phases.Offline, Phases.Raw })
        {
            if (File.Exists(spec.RunPath(model, "calibration", phase)) && File.Exists(spec.RunPath(model, "heldout", phase)))
            {
                return phase;
            }
        }

        return null;
    }

    /// <summary>Runs the stage for every model with measurements and writes <c>threshold.json</c>.</summary>
    /// <param name="spec">The decision spec.</param>
    /// <exception cref="WorkbenchException">No model has been measured.</exception>
    public static IReadOnlyList<ThresholdResult> Run(DecisionSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var results = new List<ThresholdResult>();
        foreach (var model in spec.Models)
        {
            if (BestSource(spec, model) is not { } source)
            {
                continue;
            }

            var (calConf, calCorrect) = Load(spec, model, "calibration", source);
            var (heldConf, heldCorrect) = Load(spec, model, "heldout", source);
            results.Add(Choose(model, spec.Threshold.TargetError, source, calConf, calCorrect, heldConf, heldCorrect));
        }

        if (results.Count == 0)
        {
            throw new WorkbenchException("No model has calibration and held-out measurements yet. Run 'tau measure' first.");
        }

        WorkbenchJson.WriteJson(spec.ThresholdPath, results);
        return results;
    }

    private static (double[] Confidence, bool[] Correct) Load(DecisionSpec spec, string model, string split, string phase)
    {
        var ok = WorkbenchJson.ReadJsonl<MeasuredItem>(spec.RunPath(model, split, phase))
            .Where(r => r.Error is null && r.ConfidenceMaxp is not null && r.Correct is not null).ToArray();
        return (ok.Select(r => r.ConfidenceMaxp!.Value).ToArray(), ok.Select(r => r.Correct!.Value).ToArray());
    }
}
