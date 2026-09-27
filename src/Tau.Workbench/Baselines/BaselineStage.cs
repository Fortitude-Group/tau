using System.Text.Json;
using System.Text.Json.Nodes;
using Tau.Calibration;
using Tau.Workbench.Calibrate;
using Tau.Workbench.Frontier;
using Tau.Workbench.Measure;
using Tau.Workbench.Reference;
using Tau.Workbench.Spec;

namespace Tau.Workbench.Baselines;

/// <summary>A baseline's metrics, computed with the same code as every Tau model.</summary>
public sealed record BaselineResult
{
    /// <summary>The baseline name.</summary>
    public required string Name { get; init; }

    /// <summary>The probability file, relative to the spec directory.</summary>
    public required string File { get; init; }

    /// <summary>sha256 of the probability file.</summary>
    public string? Sha256 { get; init; }

    /// <summary>Why the baseline could not be scored, when it couldn't.</summary>
    public string? Missing { get; init; }

    /// <summary>Held-out lines in the file.</summary>
    public int HeldOutItems { get; init; }

    /// <summary>Calibration lines in the file.</summary>
    public int CalibrationItems { get; init; }

    /// <summary>Held-out metrics on the baseline's own probabilities.</summary>
    public MetricSet? Raw { get; init; }

    /// <summary>Held-out metrics after a calibrator fitted on the baseline's calibration lines.</summary>
    public MetricSet? Calibrated { get; init; }

    /// <summary>The method chosen for that calibrator.</summary>
    public string? CalibrationMethod { get; init; }

    /// <summary>A note on the calibrated view (for example why it is missing).</summary>
    public string? CalibrationNote { get; init; }

    /// <summary>Under a frontier reference: raw held-out agreement with the dataset's own labels (the secondary view).</summary>
    public CountedRate? RawAgainstDatasetLabels { get; init; }

    /// <summary>Under a frontier reference: calibrated held-out agreement with the dataset's own labels.</summary>
    public CountedRate? CalibratedAgainstDatasetLabels { get; init; }
}

/// <summary>
/// The baselines stage (T029): reads sidecar probability files (<c>baselines/&lt;name&gt;.jsonl</c>, lines
/// <c>{id, split, probabilities, label}</c>) and scores them exactly as the measured models are scored,
/// raw and after a calibrator fitted on the file's own calibration lines.
/// </summary>
public static class BaselineStage
{
    /// <summary>Scores every baseline named in the spec. A missing file is reported, not fatal.</summary>
    /// <param name="spec">The decision spec.</param>
    /// <param name="reference">The labels to score against; null resolves them from the spec.</param>
    /// <exception cref="WorkbenchException">A baseline file exists but is malformed.</exception>
    public static IReadOnlyList<BaselineResult> Run(DecisionSpec spec, ReferenceLabels? reference = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return spec.Baselines.Select(name => Score(spec, name, reference)).ToArray();
    }

    /// <summary>
    /// Scores one baseline against the reference labels, raw and after a calibrator fitted on the file's
    /// calibration lines (against the same reference).
    /// </summary>
    /// <param name="spec">The decision spec.</param>
    /// <param name="name">The baseline name.</param>
    /// <param name="reference">The labels to score against; null resolves them from the spec.</param>
    /// <exception cref="WorkbenchException">The file is malformed (the line is named).</exception>
    /// <exception cref="StageBlockedException">A line's item has no frontier reference label.</exception>
    public static BaselineResult Score(DecisionSpec spec, string name, ReferenceLabels? reference = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var path = spec.BaselinePath(name);
        var rel = Path.GetRelativePath(spec.SpecDirectory, path).Replace('\\', '/');
        if (!System.IO.File.Exists(path))
        {
            return new BaselineResult { Name = name, File = rel, Missing = "The probability file has not been produced yet (sidecar baseline script)." };
        }

        var q = spec.Question;
        reference ??= ReferenceLabels.Load(spec);
        var lines = Read(path, q);
        var held = lines.Where(l => l.Split == "heldout").ToArray();
        var cal = lines.Where(l => l.Split == "calibration").ToArray();
        var heldRef = reference.ClassIndices(q, "heldout", held.Select(l => (l.Id, l.Label)).ToArray());
        var calRef = reference.ClassIndices(q, "calibration", cal.Select(l => (l.Id, l.Label)).ToArray());
        var sha = WorkbenchJson.Sha256File(path);
        var raw = MetricSet.Compute(held.Select(l => l.P).ToArray(), heldRef, q.Type == QuestionType.Score);
        double[][]? calibratedP = null;
        MetricSet? calibrated = null;
        string? method = null, note = null;
        if (cal.Length == 0)
        {
            note = "No calibration lines in the file, so no calibrated view.";
        }
        else if (held.Length == 0)
        {
            note = "No held-out lines in the file.";
        }
        else
        {
            var fit = CalibrationFitter.Fit(cal.Select(l => l.P).ToArray(), calRef, name, sha, q.Type, null,
                spec.Name, null, "n/a");
            method = fit.File.Method.ToWireString();
            calibratedP = CalibrationFitter.Apply(fit.File, held.Select(l => l.P).ToArray());
            calibrated = MetricSet.Compute(calibratedP, heldRef, q.Type == QuestionType.Score);
            note = $"Calibrated with {method} fitted on the file's {cal.Length} calibration lines{(fit.IsotonicSkipped is null ? "" : "; " + fit.IsotonicSkipped)}";
        }

        // Under a frontier reference, the secondary view: agreement with the dataset's own labels.
        CountedRate? AgainstDataset(IReadOnlyList<double[]> p) => reference.Kind == ReferenceKind.Frontier
            ? CountedRate.Of(p.Count, p.Where((v, i) => MetricSet.ArgMax(v) == held[i].Gold).Count())
            : null;
        return new BaselineResult
        {
            Name = name,
            File = rel,
            Sha256 = sha,
            HeldOutItems = held.Length,
            CalibrationItems = cal.Length,
            Raw = raw,
            Calibrated = calibrated,
            CalibrationMethod = method,
            CalibrationNote = note,
            RawAgainstDatasetLabels = AgainstDataset(held.Select(l => l.P).ToArray()),
            CalibratedAgainstDatasetLabels = calibratedP is null ? null : AgainstDataset(calibratedP),
        };
    }

    private static List<(string Id, string Split, double[] P, string Label, int Gold)> Read(string path, QuestionSpec q)
    {
        var result = new List<(string, string, double[], string, int)>();
        int lineNumber = 0;
        foreach (var line in System.IO.File.ReadLines(path))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JsonObject obj;
            try
            {
                obj = JsonNode.Parse(line) as JsonObject ?? throw new WorkbenchException($"{path}:{lineNumber} is not a JSON object.");
            }
            catch (JsonException e)
            {
                throw new WorkbenchException($"{path}:{lineNumber} does not parse: {e.Message}", e);
            }

            string split = (obj["split"] as JsonValue)?.ToString() ?? "";
            string label = obj["label"] is JsonValue lv ? (lv.TryGetValue<string>(out var s) ? s : lv.ToJsonString()) : "";
            int gold = q.ClassIndex(label) ?? throw new WorkbenchException($"{path}:{lineNumber} has label '{label}', which is not an answer to the question.");
            if (obj["probabilities"] is not JsonObject probs)
            {
                throw new WorkbenchException($"{path}:{lineNumber} has no 'probabilities' object.");
            }

            var p = new double[q.Classes.Count];
            for (int i = 0; i < p.Length; i++)
            {
                if (probs[q.Classes[i]] is not JsonValue v || !v.TryGetValue<double>(out p[i]) || !double.IsFinite(p[i]) || p[i] < 0)
                {
                    throw new WorkbenchException($"{path}:{lineNumber} has no valid probability for '{q.Classes[i]}'.");
                }
            }

            string id = (obj["id"] as JsonValue)?.ToString() ?? "";
            result.Add((id, split, p, label, gold));
        }

        return result;
    }
}
