namespace Tau.Workbench.Frontier;

/// <summary>One line of a pending batch file: everything the answerer needs for one item.</summary>
/// <param name="Key">The cache key, <c>&lt;dataset-rev&gt;:&lt;item-id&gt;:&lt;prompt-version&gt;</c>.</param>
/// <param name="ItemId">The held-out item id.</param>
/// <param name="PromptVersion">The prompt version the prompt was rendered with.</param>
/// <param name="Prompt">The full per-item prompt.</param>
/// <param name="Allowed">The only acceptable answers.</param>
public sealed record BatchLine(string Key, string ItemId, string PromptVersion, string Prompt, IReadOnlyList<string> Allowed);

/// <summary>One accepted frontier answer, with its provenance.</summary>
/// <param name="Key">The cache key.</param>
/// <param name="ItemId">The held-out item id.</param>
/// <param name="PromptVersion">The prompt version.</param>
/// <param name="Answer">The answer, trimmed, and one of the allowed answers.</param>
/// <param name="Model">The frontier model that answered.</param>
/// <param name="ProducedBy">How it was produced (always an interactive session, never the API).</param>
/// <param name="Date">When it was produced.</param>
/// <param name="InputChars">Prompt characters (for the token estimate).</param>
/// <param name="OutputChars">Answer characters (for the token estimate).</param>
public sealed record CachedAnswer(
    string Key, string ItemId, string PromptVersion, string Answer, string Model, string ProducedBy, string Date, int InputChars, int OutputChars);

/// <summary>A human override of an item's frontier label.</summary>
/// <param name="ItemId">The held-out item id.</param>
/// <param name="Answer">The label that replaces the frontier answer.</param>
/// <param name="By">Who made the override.</param>
/// <param name="Date">When.</param>
/// <param name="Reason">Why.</param>
public sealed record LabelOverride(string ItemId, string Answer, string By, string Date, string Reason);

/// <summary>A cache or override line that was not accepted, and why.</summary>
/// <param name="File">Which file (cache or overrides).</param>
/// <param name="Line">1-based line number.</param>
/// <param name="Key">The line's key or item id, when readable.</param>
/// <param name="Answer">The line's answer, when readable.</param>
/// <param name="Reason">Why it was rejected.</param>
public sealed record RejectedLine(string File, int Line, string? Key, string? Answer, string Reason);

/// <summary>A rate with its numerator and denominator, so no percentage is shown without its count.</summary>
/// <param name="N">The denominator.</param>
/// <param name="Count">The numerator.</param>
/// <param name="Rate">Count / N, or null when N is 0.</param>
public sealed record CountedRate(int N, int Count, double? Rate)
{
    /// <summary>Builds a rate, null when <paramref name="n"/> is 0.</summary>
    /// <param name="n">The denominator.</param>
    /// <param name="count">The numerator.</param>
    public static CountedRate Of(int n, int count) => new(n, count, n == 0 ? null : (double)count / n);
}

/// <summary>Character tallies over accepted answers of one prompt version.</summary>
/// <param name="Answers">How many answers the tallies cover.</param>
/// <param name="InputChars">Total prompt characters.</param>
/// <param name="OutputChars">Total answer characters.</param>
public sealed record CharTally(int Answers, long InputChars, long OutputChars);

/// <summary>What <c>tau label</c> found and did, written to <c>frontier/label-summary.json</c>.</summary>
public sealed record LabelSummary
{
    /// <summary>The dataset revision used in cache keys.</summary>
    public required string DatasetRevision { get; init; }

    /// <summary>The frontier model named in the spec.</summary>
    public required string FrontierModel { get; init; }

    /// <summary>Held-out items in the prepared split.</summary>
    public required int HeldOutItems { get; init; }

    /// <summary>Held-out items eligible for frontier answers (the first 1,000 at most, in file order).</summary>
    public required int EligibleItems { get; init; }

    /// <summary>Items that get the alternative wording.</summary>
    public required int AltSubsetItems { get; init; }

    /// <summary>Items still waiting for an answer, per prompt version.</summary>
    public required IReadOnlyDictionary<string, int> Pending { get; init; }

    /// <summary>Accepted answers, per prompt version.</summary>
    public required IReadOnlyDictionary<string, int> Cached { get; init; }

    /// <summary>Pending batch files written by this run, relative to the spec directory.</summary>
    public required IReadOnlyList<string> PendingBatches { get; init; }

    /// <summary>Cache or override lines that were rejected, with the reason.</summary>
    public required IReadOnlyList<RejectedLine> Rejected { get; init; }

    /// <summary>Valid cache lines that repeated an already-accepted key (the first answer is kept).</summary>
    public required int DuplicateCacheLines { get; init; }

    /// <summary>Accepted human overrides.</summary>
    public required int Overridden { get; init; }

    /// <summary>Items where the (unoverridden) primary frontier answer differs from gold.</summary>
    public required CountedRate LabelNoise { get; init; }

    /// <summary>Items where the primary and alternative wordings gave the same answer.</summary>
    public required CountedRate PromptAgreement { get; init; }

    /// <summary>Character tallies per prompt version, for the cost estimate.</summary>
    public required IReadOnlyDictionary<string, CharTally> Chars { get; init; }

    /// <summary>Distinct producing models in the cache.</summary>
    public required IReadOnlyList<string> ProvenanceModels { get; init; }

    /// <summary>Distinct "produced by" statements in the cache.</summary>
    public required IReadOnlyList<string> ProvenanceProducedBy { get; init; }

    /// <summary>Earliest and latest answer dates in the cache.</summary>
    public required IReadOnlyList<string> ProvenanceDates { get; init; }

    /// <summary>The total pending across prompt versions.</summary>
    public int TotalPending => Pending.Values.Sum();
}
