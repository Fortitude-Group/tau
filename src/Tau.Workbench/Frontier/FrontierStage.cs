using System.Text.Json;
using System.Text.Json.Nodes;
using Tau.Workbench.Data;
using Tau.Workbench.Spec;

namespace Tau.Workbench.Frontier;

/// <summary>
/// Everything known about frontier answers for one dataset after ingesting the cache and overrides.
/// </summary>
public sealed class FrontierState
{
    internal FrontierState(
        IReadOnlyList<DatasetItem> eligible,
        IReadOnlyList<DatasetItem> calibrationEligible,
        IReadOnlySet<string> altSubset,
        IReadOnlyDictionary<string, CachedAnswer> accepted,
        IReadOnlyDictionary<string, LabelOverride> overrides,
        IReadOnlyList<RejectedLine> rejected,
        int duplicates,
        string revision)
    {
        Eligible = eligible;
        CalibrationEligible = calibrationEligible;
        AltSubset = altSubset;
        Accepted = accepted;
        Overrides = overrides;
        Rejected = rejected;
        DuplicateCacheLines = duplicates;
        DatasetRevision = revision;
    }

    /// <summary>The held-out items eligible for frontier answers, in file order (at most 1,000).</summary>
    public IReadOnlyList<DatasetItem> Eligible { get; }

    /// <summary>
    /// The calibration items eligible for primary-prompt answers, in file order (at most 1,000). Empty unless
    /// the spec scores against the frontier model, because only then do the calibrators need frontier labels.
    /// </summary>
    public IReadOnlyList<DatasetItem> CalibrationEligible { get; }

    /// <summary>Ids of the items that also get the alternative wording.</summary>
    public IReadOnlySet<string> AltSubset { get; }

    /// <summary>Accepted answers keyed by cache key.</summary>
    public IReadOnlyDictionary<string, CachedAnswer> Accepted { get; }

    /// <summary>Accepted overrides keyed by item id (the last valid line for an item wins).</summary>
    public IReadOnlyDictionary<string, LabelOverride> Overrides { get; }

    /// <summary>Rejected cache and override lines.</summary>
    public IReadOnlyList<RejectedLine> Rejected { get; }

    /// <summary>Valid cache lines that repeated an accepted key.</summary>
    public int DuplicateCacheLines { get; }

    /// <summary>The dataset revision used in cache keys.</summary>
    public string DatasetRevision { get; }

    /// <summary>The accepted answer for an item and prompt version, before any override.</summary>
    /// <param name="itemId">The item id.</param>
    /// <param name="promptVersion">The prompt version.</param>
    public CachedAnswer? Answer(string itemId, string promptVersion) =>
        Accepted.GetValueOrDefault(FrontierStage.Key(DatasetRevision, itemId, promptVersion));

    /// <summary>The label the cascade uses for an item: the human override if there is one, else the primary answer.</summary>
    /// <param name="itemId">The item id.</param>
    /// <param name="promptVersion">The primary prompt version.</param>
    public string? EffectiveAnswer(string itemId, string promptVersion) =>
        Overrides.TryGetValue(itemId, out var o) ? o.Answer : Answer(itemId, promptVersion)?.Answer;

    /// <summary>
    /// Character tallies over the accepted held-out answers of one prompt version. Calibration answers are
    /// left out: cost is per decision served, and labelling the calibration split is a one-off set-up cost.
    /// </summary>
    /// <param name="promptVersion">The prompt version.</param>
    public CharTally HeldOutChars(string promptVersion)
    {
        var heldOut = Eligible.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        var answers = Accepted.Values.Where(a => a.PromptVersion == promptVersion && heldOut.Contains(a.ItemId)).ToArray();
        return new CharTally(answers.Length, answers.Sum(a => (long)a.InputChars), answers.Sum(a => (long)a.OutputChars));
    }
}

/// <summary>
/// The label stage (research R-05): ingests <c>frontier/cache.jsonl</c> and <c>frontier/overrides.jsonl</c>,
/// validates every answer against the question's allowed answers, exports the still-missing items as
/// pending batch files, and writes <c>frontier/label-summary.json</c>. It never calls any API, never
/// exports an item whose answer is cached, and never exports more than 1,000 held-out items per dataset.
/// </summary>
public static class FrontierStage
{
    /// <summary>The cache key for an item: <c>&lt;dataset-rev&gt;:&lt;item-id&gt;:&lt;prompt-version&gt;</c>.</summary>
    /// <param name="revision">The dataset revision.</param>
    /// <param name="itemId">The item id.</param>
    /// <param name="promptVersion">The prompt version.</param>
    public static string Key(string revision, string itemId, string promptVersion) => $"{revision}:{itemId}:{promptVersion}";

    /// <summary>
    /// Chooses the alternative-wording subset deterministically: items ordered by
    /// sha256("42:" + id), first <paramref name="count"/> taken, returned in the input order. The choice
    /// depends only on the ids, never on the runtime's random number generator.
    /// </summary>
    /// <param name="ids">Candidate ids.</param>
    /// <param name="count">How many to take.</param>
    public static IReadOnlyList<string> AltSubset(IReadOnlyList<string> ids, int count)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var chosen = ids
            .OrderBy(id => WorkbenchJson.Sha256Text(FrontierLimits.AltSubsetSeed + ":" + id), StringComparer.Ordinal)
            .ThenBy(id => id, StringComparer.Ordinal)
            .Take(Math.Max(0, count))
            .ToHashSet(StringComparer.Ordinal);
        return ids.Where(chosen.Contains).ToArray();
    }

    /// <summary>Ingests the cache and overrides without exporting anything.</summary>
    /// <param name="spec">The decision spec.</param>
    /// <param name="manifest">The dataset manifest (for the cache revision).</param>
    /// <param name="heldOut">The held-out split, in file order.</param>
    /// <param name="calibration">
    /// The calibration split, in file order. Required when the spec scores against the frontier model
    /// (<c>data.reference: frontier</c>), and ignored otherwise.
    /// </param>
    /// <exception cref="ArgumentException">The spec scores against the frontier model and no calibration split was given.</exception>
    public static FrontierState LoadState(DecisionSpec spec, DatasetManifest manifest, IReadOnlyList<DatasetItem> heldOut, IReadOnlyList<DatasetItem>? calibration = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(heldOut);
        bool frontierReference = spec.Data.Reference == ReferenceKind.Frontier;
        if (frontierReference && calibration is null)
        {
            throw new ArgumentException("The spec scores against the frontier model, so the calibration split is needed too.", nameof(calibration));
        }

        var revision = manifest.CacheRevision;
        var eligible = heldOut.Take(FrontierLimits.MaxHeldOutItems).ToArray();
        DatasetItem[] calEligible = frontierReference ? calibration!.Take(FrontierLimits.MaxCalibrationItems).ToArray() : [];
        var alt = AltSubset(eligible.Select(i => i.Id).ToArray(), spec.Frontier.AltSubset).ToHashSet(StringComparer.Ordinal);
        var byId = eligible.ToDictionary(i => i.Id, StringComparer.Ordinal);
        var calById = calEligible.ToDictionary(i => i.Id, StringComparer.Ordinal);
        if (calById.Keys.FirstOrDefault(byId.ContainsKey) is { } id)
        {
            throw new WorkbenchException($"Item id '{id}' is in both the held-out and the calibration split, so a cache key can't say which split it belongs to. Re-prepare the data.");
        }
        var allowed = spec.Question.AllowedAnswers.ToHashSet(StringComparer.Ordinal);
        var rejected = new List<RejectedLine>();
        var accepted = new Dictionary<string, CachedAnswer>(StringComparer.Ordinal);
        int duplicates = 0;

        foreach (var (lineNumber, obj, parseError) in ReadObjects(spec.CachePath))
        {
            string? key = Str(obj, "key");
            string? answerRaw = Str(obj, "answer");
            string? Reject(string reason)
            {
                rejected.Add(new RejectedLine("cache.jsonl", lineNumber, key, answerRaw, reason));
                return null;
            }

            if (parseError is not null)
            {
                Reject(parseError);
                continue;
            }

            string? itemId = Str(obj, "item_id");
            string? pv = Str(obj, "prompt_version");
            string? model = Str(obj, "model");
            string? producedBy = Str(obj, "produced_by");
            string? date = Str(obj, "date");
            if (key is null || itemId is null || pv is null)
            {
                Reject("missing key, item_id or prompt_version");
                continue;
            }

            if (pv != spec.Frontier.PromptVersion && pv != spec.Frontier.AltPromptVersion)
            {
                Reject($"prompt version '{pv}' is not one this spec uses");
                continue;
            }

            if (key != Key(revision, itemId, pv))
            {
                Reject($"key does not match {Key(revision, itemId, pv)} (wrong dataset revision, item or prompt version)");
                continue;
            }

            if (!(byId.TryGetValue(itemId, out var item) || (pv == spec.Frontier.PromptVersion && calById.TryGetValue(itemId, out item)))
                || (pv == spec.Frontier.AltPromptVersion && !alt.Contains(itemId)))
            {
                Reject("item is not in the exported set for this prompt version");
                continue;
            }

            if (answerRaw is null || !allowed.Contains(answerRaw.Trim()))
            {
                Reject("answer is not one of the allowed answers (after trimming)");
                continue;
            }

            if (string.IsNullOrWhiteSpace(model) || string.IsNullOrWhiteSpace(producedBy) || string.IsNullOrWhiteSpace(date))
            {
                Reject("provenance is incomplete: model, produced_by and date are all required");
                continue;
            }

            if (accepted.ContainsKey(key))
            {
                duplicates++;
                continue;
            }

            var answer = answerRaw.Trim();
            int inputChars = Int(obj, "input_chars") ?? PromptTemplates.Render(pv, spec.Question, item.Text).Length;
            int outputChars = Int(obj, "output_chars") ?? answer.Length;
            accepted[key] = new CachedAnswer(key, itemId, pv, answer, model, producedBy, date, inputChars, outputChars);
        }

        var overrides = new Dictionary<string, LabelOverride>(StringComparer.Ordinal);
        foreach (var (lineNumber, obj, parseError) in ReadObjects(spec.OverridesPath))
        {
            string? itemId = Str(obj, "item_id");
            string? answer = Str(obj, "answer")?.Trim();
            string? by = Str(obj, "by");
            string? date = Str(obj, "date");
            string reason = Str(obj, "reason") ?? "";
            string? problem = parseError
                ?? (itemId is null || !(byId.ContainsKey(itemId) || calById.ContainsKey(itemId))
                    ? $"item_id is missing or not an eligible held-out{(frontierReference ? " or calibration" : "")} item"
                : answer is null || !allowed.Contains(answer) ? "answer is not one of the allowed answers"
                : string.IsNullOrWhiteSpace(by) || string.IsNullOrWhiteSpace(date) ? "provenance is incomplete: by and date are required"
                : null);
            if (problem is not null)
            {
                rejected.Add(new RejectedLine("overrides.jsonl", lineNumber, itemId, answer, problem));
                continue;
            }

            overrides[itemId!] = new LabelOverride(itemId!, answer!, by!, date!, reason);
        }

        return new FrontierState(eligible, calEligible, alt, accepted, overrides, rejected, duplicates, revision);
    }

    /// <summary>
    /// Runs the label stage: ingest, export pending batches (replacing any earlier pending files for the
    /// same prompt version), and write the summary. Held-out batches are <c>batch-NNN.jsonl</c>; under a
    /// frontier reference the calibration split's primary-prompt batches are <c>batch-calibration-NNN.jsonl</c>
    /// in the same directory, so no batch mixes the two splits.
    /// </summary>
    /// <param name="spec">The decision spec.</param>
    /// <param name="manifest">The dataset manifest.</param>
    /// <param name="heldOut">The held-out split, in file order.</param>
    /// <param name="calibration">The calibration split, in file order (required under a frontier reference, ignored otherwise).</param>
    /// <returns>The summary. <see cref="LabelSummary.TotalPending"/> above 0 means the CLI exits 2.</returns>
    public static LabelSummary Run(DecisionSpec spec, DatasetManifest manifest, IReadOnlyList<DatasetItem> heldOut, IReadOnlyList<DatasetItem>? calibration = null)
    {
        var state = LoadState(spec, manifest, heldOut, calibration);
        var pv1 = spec.Frontier.PromptVersion;
        var alt = spec.Frontier.AltPromptVersion;
        var pending = new Dictionary<string, int>(StringComparer.Ordinal) { [pv1] = 0, [alt] = 0 };
        var cached = new Dictionary<string, int>(StringComparer.Ordinal) { [pv1] = 0, [alt] = 0 };
        var splits = new Dictionary<string, SplitLabelCounts>(StringComparer.Ordinal);
        var batches = new List<string>();
        foreach (var dir in new[] { pv1, alt }.Select(spec.PendingDirectory).Where(Directory.Exists))
        {
            foreach (var old in Directory.EnumerateFiles(dir, "batch-*.jsonl"))
            {
                File.Delete(old);
            }
        }

        var jobs = new List<(string Split, int Items, IReadOnlyList<DatasetItem> Eligible, (string Pv, IReadOnlyList<DatasetItem> Wanted)[] Versions)>
        {
            ("heldout", heldOut.Count, state.Eligible, [(pv1, state.Eligible), (alt, state.Eligible.Where(i => state.AltSubset.Contains(i.Id)).ToArray())]),
        };
        if (spec.Data.Reference == ReferenceKind.Frontier)
        {
            jobs.Add(("calibration", calibration!.Count, state.CalibrationEligible, [(pv1, state.CalibrationEligible)]));
        }

        // Primary-prompt batches first (held-out, then calibration), then the alternative wording.
        foreach (var pv in new[] { pv1, alt })
        {
            foreach (var job in jobs)
            {
                if (job.Versions.FirstOrDefault(v => v.Pv == pv).Wanted is not { } wanted)
                {
                    continue;
                }

                var missing = wanted.Where(i => state.Answer(i.Id, pv) is null).ToArray();
                pending[pv] += missing.Length;
                cached[pv] += wanted.Count - missing.Length;
                var prefix = job.Split == "heldout" ? "batch-" : $"batch-{job.Split}-";
                for (int b = 0; b * FrontierLimits.BatchSize < missing.Length; b++)
                {
                    var path = Path.Combine(spec.PendingDirectory(pv), $"{prefix}{b + 1:000}.jsonl");
                    WorkbenchJson.WriteJsonl(path, missing.Skip(b * FrontierLimits.BatchSize).Take(FrontierLimits.BatchSize).Select(i =>
                        new BatchLine(Key(state.DatasetRevision, i.Id, pv), i.Id, pv, PromptTemplates.Render(pv, spec.Question, i.Text), spec.Question.AllowedAnswers, job.Split)));
                    batches.Add(Path.GetRelativePath(spec.SpecDirectory, path).Replace('\\', '/'));
                }
            }
        }

        foreach (var job in jobs)
        {
            splits[job.Split] = new SplitLabelCounts(
                job.Items,
                job.Eligible.Count,
                job.Versions.ToDictionary(v => v.Pv, v => v.Wanted.Count(i => state.Answer(i.Id, v.Pv) is null), StringComparer.Ordinal),
                job.Versions.ToDictionary(v => v.Pv, v => v.Wanted.Count(i => state.Answer(i.Id, v.Pv) is not null), StringComparer.Ordinal));
        }

        var summary = new LabelSummary
        {
            DatasetRevision = state.DatasetRevision,
            FrontierModel = spec.Frontier.Model,
            Reference = spec.Data.Reference,
            HeldOutItems = heldOut.Count,
            EligibleItems = state.Eligible.Count,
            AltSubsetItems = state.AltSubset.Count,
            Pending = pending,
            Cached = cached,
            Splits = splits,
            PendingBatches = batches,
            Rejected = state.Rejected,
            DuplicateCacheLines = state.DuplicateCacheLines,
            Overridden = state.Overrides.Count,
            LabelNoise = LabelNoise(spec, state),
            PromptAgreement = Agreement(spec, state),
            Chars = new[] { pv1, alt }.ToDictionary(pv => pv, state.HeldOutChars, StringComparer.Ordinal),
            ProvenanceModels = state.Accepted.Values.Select(a => a.Model).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            ProvenanceProducedBy = state.Accepted.Values.Select(a => a.ProducedBy).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            ProvenanceDates = state.Accepted.Count == 0
                ? []
                : [state.Accepted.Values.Select(a => a.Date).Order(StringComparer.Ordinal).First(),
                   state.Accepted.Values.Select(a => a.Date).Order(StringComparer.Ordinal).Last()],
        };
        WorkbenchJson.WriteJson(spec.LabelSummaryPath, summary);
        return summary;
    }

    /// <summary>Disagreement between the primary frontier answer (before overrides) and gold.</summary>
    /// <param name="spec">The decision spec.</param>
    /// <param name="state">The ingested state.</param>
    public static CountedRate LabelNoise(DecisionSpec spec, FrontierState state)
    {
        int n = 0, disagree = 0;
        foreach (var item in state.Eligible)
        {
            if (state.Answer(item.Id, spec.Frontier.PromptVersion) is { } a)
            {
                n++;
                if (spec.Question.ClassIndex(a.Answer) != spec.Question.ClassIndex(item.Label))
                {
                    disagree++;
                }
            }
        }

        return CountedRate.Of(n, disagree);
    }

    /// <summary>Agreement between the two wordings on items answered under both.</summary>
    /// <param name="spec">The decision spec.</param>
    /// <param name="state">The ingested state.</param>
    public static CountedRate Agreement(DecisionSpec spec, FrontierState state)
    {
        int n = 0, agree = 0;
        foreach (var id in state.AltSubset)
        {
            if (state.Answer(id, spec.Frontier.PromptVersion) is { } a && state.Answer(id, spec.Frontier.AltPromptVersion) is { } b)
            {
                n++;
                if (spec.Question.ClassIndex(a.Answer) == spec.Question.ClassIndex(b.Answer))
                {
                    agree++;
                }
            }
        }

        return CountedRate.Of(n, agree);
    }

    private static IEnumerable<(int Line, JsonObject? Obj, string? Error)> ReadObjects(string path)
    {
        if (!File.Exists(path))
        {
            yield break;
        }

        int lineNumber = 0;
        foreach (var line in File.ReadLines(path))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JsonObject? obj = null;
            string? error = null;
            try
            {
                obj = JsonNode.Parse(line) as JsonObject;
                error = obj is null ? "line is not a JSON object" : null;
            }
            catch (JsonException e)
            {
                error = $"line is not valid JSON: {e.Message}";
            }

            yield return (lineNumber, obj, error);
        }
    }

    private static string? Str(JsonObject? obj, string name) =>
        obj?[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static int? Int(JsonObject? obj, string name) =>
        obj?[name] is JsonValue v && v.TryGetValue<int>(out var i) && i >= 0 ? i : null;
}
