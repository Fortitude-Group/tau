using Tau.Workbench.Data;
using Tau.Workbench.Frontier;
using Tau.Workbench.Measure;
using Tau.Workbench.Spec;

namespace Tau.Workbench.Reference;

/// <summary>
/// The one place that says what each item's reference label is, given the spec: the dataset's gold label
/// (<c>data.reference: gold</c>, the default), or the frontier model's cached primary-prompt answer after
/// human overrides (<c>data.reference: frontier</c>). Measure, calibrate, threshold, cascade, baselines and
/// the report all score through it. A missing frontier label is an error that names the fix, never an
/// item dropped to make the numbers work.
/// </summary>
public sealed class ReferenceLabels
{
    private readonly IReadOnlyDictionary<string, string> gold;

    private ReferenceLabels(ReferenceKind kind, string promptVersion, FrontierState? frontier, IReadOnlyDictionary<string, string> gold)
    {
        Kind = kind;
        PromptVersion = promptVersion;
        Frontier = frontier;
        this.gold = gold;
    }

    /// <summary>What the labels are: gold or frontier.</summary>
    public ReferenceKind Kind { get; }

    /// <summary>The primary prompt version whose answers are the frontier reference.</summary>
    public string PromptVersion { get; }

    /// <summary>The ingested frontier answers (held-out and calibration) under a frontier reference; null under gold.</summary>
    public FrontierState? Frontier { get; }

    /// <summary>
    /// Resolves the reference for a spec. Under gold nothing is read. Under frontier the prepared held-out and
    /// calibration splits are loaded (and checked against the manifest) and the frontier cache is ingested.
    /// </summary>
    /// <param name="spec">The decision spec.</param>
    /// <param name="manifest">The dataset manifest; loaded from the spec when null and needed.</param>
    public static ReferenceLabels Load(DecisionSpec spec, DatasetManifest? manifest = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (spec.Data.Reference == ReferenceKind.Gold)
        {
            return new ReferenceLabels(ReferenceKind.Gold, spec.Frontier.PromptVersion, null, new Dictionary<string, string>());
        }

        manifest ??= DatasetManifest.Load(spec.ManifestPath);
        var heldOut = PreparedDataset.LoadSplit(spec, manifest, "heldout");
        var calibration = PreparedDataset.LoadSplit(spec, manifest, "calibration");
        return FromState(spec, FrontierStage.LoadState(spec, manifest, heldOut, calibration), heldOut.Concat(calibration));
    }

    /// <summary>Builds a frontier reference from an already ingested state.</summary>
    /// <param name="spec">The decision spec (its primary prompt version names the reference answers).</param>
    /// <param name="state">The ingested frontier state.</param>
    /// <param name="items">The prepared items, for the dataset's own labels (the secondary gold view).</param>
    public static ReferenceLabels FromState(DecisionSpec spec, FrontierState state, IEnumerable<DatasetItem> items)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(items);
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            labels[item.Id] = item.Label;
        }

        return new ReferenceLabels(ReferenceKind.Frontier, spec.Frontier.PromptVersion, state, labels);
    }

    /// <summary>The reference label for one item, or null when a frontier reference has no answer for it.</summary>
    /// <param name="itemId">The item id.</param>
    /// <param name="goldLabel">The item's gold label (returned as is under a gold reference).</param>
    public string? LabelFor(string itemId, string goldLabel) =>
        Kind == ReferenceKind.Gold ? goldLabel : Frontier!.EffectiveAnswer(itemId, PromptVersion);

    /// <summary>The dataset's own label for an item, under a frontier reference (null under gold, or for an unknown id).</summary>
    /// <param name="itemId">The item id.</param>
    public string? DatasetLabel(string itemId) => gold.GetValueOrDefault(itemId);

    /// <summary>
    /// Returns the records scored against the reference: under gold they are returned unchanged; under
    /// frontier each record's <see cref="MeasuredItem.Gold"/> becomes the frontier label and
    /// <see cref="MeasuredItem.Correct"/> is recomputed from it. Records on disk always keep the dataset's label.
    /// </summary>
    /// <param name="question">The question.</param>
    /// <param name="split">The split the records belong to (named in the error).</param>
    /// <param name="records">The records.</param>
    /// <exception cref="StageBlockedException">An item has no frontier reference label.</exception>
    public IReadOnlyList<MeasuredItem> Apply(QuestionSpec question, string split, IReadOnlyList<MeasuredItem> records)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(records);
        if (Kind == ReferenceKind.Gold)
        {
            return records;
        }

        var missing = new List<string>();
        var result = new MeasuredItem[records.Count];
        for (int i = 0; i < records.Count; i++)
        {
            var r = records[i];
            if (LabelFor(r.ItemId, r.Gold) is not { } label)
            {
                missing.Add(r.ItemId);
                result[i] = r;
                continue;
            }

            result[i] = r with { Gold = label, Correct = r.Answer is null ? null : question.ClassIndex(r.Answer) == question.ClassIndex(label) };
        }

        ThrowIfMissing(split, missing, records.Count);
        return result;
    }

    /// <summary>The reference class index of each (id, gold label) pair, for probability files such as baselines.</summary>
    /// <param name="question">The question.</param>
    /// <param name="split">The split the items belong to (named in the error).</param>
    /// <param name="items">Item ids with their gold labels.</param>
    /// <exception cref="StageBlockedException">An item has no frontier reference label.</exception>
    public int[] ClassIndices(QuestionSpec question, string split, IReadOnlyList<(string Id, string Gold)> items)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(items);
        var missing = new List<string>();
        var result = new int[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            if (LabelFor(items[i].Id, items[i].Gold) is { } label && question.ClassIndex(label) is { } index)
            {
                result[i] = index;
            }
            else
            {
                missing.Add(items[i].Id);
            }
        }

        ThrowIfMissing(split, missing, items.Count);
        return result;
    }

    /// <summary>Checks that every item of a split has a reference label, before any expensive work starts.</summary>
    /// <param name="split">The split.</param>
    /// <param name="items">The split's items.</param>
    /// <exception cref="StageBlockedException">An item has no frontier reference label.</exception>
    public void Require(string split, IReadOnlyList<DatasetItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        ThrowIfMissing(split, items.Where(i => LabelFor(i.Id, i.Label) is null).Select(i => i.Id).ToList(), items.Count);
    }

    private static void ThrowIfMissing(string split, List<string> missing, int total)
    {
        if (missing.Count > 0)
        {
            throw new StageBlockedException(
                $"{Fmt.Int(missing.Count)} of {Fmt.Int(total)} {split} item(s) have no frontier reference label (for example {string.Join(", ", missing.Take(3))}). "
                + $"This spec scores against the frontier model's answers (data.reference: frontier), so every scored item needs one, and at most {Fmt.Int(FrontierLimits.MaxCalibrationItems)} items per split can have one. "
                + "Run 'tau label', answer the pending batches, and run 'tau label' again. No item is dropped to make the numbers work.");
        }
    }
}
