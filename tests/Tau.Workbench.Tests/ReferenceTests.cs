using System.Text.Json.Nodes;
using Tau.Client;
using Tau.Workbench.Cascade;
using Tau.Workbench.Data;
using Tau.Workbench.Frontier;
using Tau.Workbench.Measure;
using Tau.Workbench.Reference;
using Tau.Workbench.Spec;
using Tau.Workbench.Threshold;

namespace Tau.Workbench.Tests;

/// <summary>The <c>data.reference</c> setting: spec validation, calibration-split labelling, resolution and the cascade.</summary>
public sealed class ReferenceTests
{
    /// <summary>A repo whose spec scores against the frontier model.</summary>
    internal static TestRepo FrontierRepo(int calibration = 300, int heldOut = 50, int classes = 4, int altSubset = 10, bool synthetic = false,
        string models = "[model-a]", string baselines = "[]", double targetError = 0.1)
    {
        var repo = TestRepo.Create(calibration: calibration, heldOut: heldOut, classes: classes, altSubset: altSubset, synthetic: synthetic,
            models: models, baselines: baselines, targetError: targetError);
        repo.WriteSpec(File.ReadAllText(repo.SpecPath).Replace("label_field: label", "label_field: label\n  reference: frontier", StringComparison.Ordinal));
        return repo;
    }

    internal static IReadOnlyList<DatasetItem> Split(TestRepo repo, string split) => PreparedDataset.LoadSplit(repo.Spec, repo.Manifest, split);

    internal static List<BatchLine> Batches(TestRepo repo, string pv, string pattern = "batch-*.jsonl") =>
        Directory.Exists(repo.Spec.PendingDirectory(pv))
            ? Directory.EnumerateFiles(repo.Spec.PendingDirectory(pv), pattern).Order(StringComparer.Ordinal).SelectMany(WorkbenchJson.ReadJsonl<BatchLine>).ToList()
            : [];

    /// <summary>Appends cache lines answering <paramref name="lines"/>, with the prompt length as the input characters.</summary>
    internal static void Answer(TestRepo repo, IEnumerable<BatchLine> lines, Func<BatchLine, string> answer)
    {
        Directory.CreateDirectory(repo.Spec.FrontierDirectory);
        File.AppendAllLines(repo.Spec.CachePath, lines.Select(l => new JsonObject
        {
            ["key"] = l.Key, ["item_id"] = l.ItemId, ["prompt_version"] = l.PromptVersion, ["answer"] = answer(l),
            ["model"] = "frontier-x", ["produced_by"] = "Claude Code session (subagent), not the API", ["date"] = "2026-09-27",
            ["input_chars"] = l.Prompt.Length, ["output_chars"] = 1,
        }.ToJsonString()));
    }

    [Theory]
    [InlineData("banking77", ReferenceKind.Gold)]
    [InlineData("support-tickets", ReferenceKind.Frontier)]
    public void TheCommittedExamplesNameTheirReference(string example, ReferenceKind expected)
    {
        var root = DecisionSpec.FindRepoRoot(AppContext.BaseDirectory)!;
        Assert.Equal(expected, DecisionSpec.Load(Path.Combine(root, "examples", example, "decision.yaml")).Data.Reference);
    }

    [Fact]
    public void ReferenceDefaultsToGoldAndAcceptsFrontier()
    {
        using var gold = TestRepo.Create();
        Assert.Equal(ReferenceKind.Gold, gold.Spec.Data.Reference);
        using var frontier = FrontierRepo();
        Assert.Equal(ReferenceKind.Frontier, frontier.Spec.Data.Reference);
        frontier.WriteSpec(File.ReadAllText(frontier.SpecPath).Replace("reference: frontier", "reference: gold", StringComparison.Ordinal));
        Assert.Equal(ReferenceKind.Gold, frontier.Spec.Data.Reference);
    }

    [Theory]
    [InlineData("silver")]
    [InlineData("Frontier")]
    [InlineData("")]
    public void AnUnknownReferenceIsRejected(string value)
    {
        using var repo = FrontierRepo();
        repo.WriteSpec(File.ReadAllText(repo.SpecPath).Replace("reference: frontier", $"reference: \"{value}\"", StringComparison.Ordinal));
        var e = Assert.Throws<SpecValidationException>(() => DecisionSpec.Load(repo.SpecPath));
        Assert.Contains(e.Problems, p => p.Contains($"data.reference '{value}' must be gold", StringComparison.Ordinal));
    }

    [Fact]
    public void UnderGoldNoCalibrationItemIsExported()
    {
        using var repo = TestRepo.Create(heldOut: 30, altSubset: 5);
        var summary = FrontierStage.Run(repo.Spec, repo.Manifest, Split(repo, "heldout"), Split(repo, "calibration"));
        Assert.Equal(["heldout"], summary.Splits.Keys);
        Assert.Empty(Batches(repo, "v1", "batch-calibration-*.jsonl"));
        Assert.Equal(ReferenceKind.Gold, summary.Reference);
    }

    [Fact]
    public void UnderFrontierTheCalibrationSplitIsExportedWithThePrimaryPromptOnlyAndCapped()
    {
        using var repo = FrontierRepo(calibration: 1234, heldOut: 50, altSubset: 10);
        var summary = FrontierStage.Run(repo.Spec, repo.Manifest, Split(repo, "heldout"), Split(repo, "calibration"));

        var cal = summary.Splits["calibration"];
        Assert.Equal(1234, cal.Items);
        Assert.Equal(1000, cal.Eligible);
        Assert.Equal(1000, cal.Pending["v1"]);
        Assert.Equal(0, cal.Cached["v1"]);
        Assert.False(cal.Pending.ContainsKey("v1-alt")); // never the alternative wording
        Assert.Equal(50, summary.Splits["heldout"].Pending["v1"]);
        Assert.Equal(10, summary.Splits["heldout"].Pending["v1-alt"]);
        Assert.Equal(1050, summary.Pending["v1"]);
        Assert.Equal(1060, summary.TotalPending);
        Assert.Equal(
            ["frontier/pending/v1/batch-001.jsonl", .. Enumerable.Range(1, 5).Select(b => $"frontier/pending/v1/batch-calibration-{b:000}.jsonl"), "frontier/pending/v1-alt/batch-001.jsonl"],
            summary.PendingBatches);

        var calLines = Batches(repo, "v1", "batch-calibration-*.jsonl");
        Assert.Equal(1000, calLines.Count);
        Assert.All(calLines, l => Assert.Equal("calibration", l.Split));
        Assert.Equal("0123456789ab:c0:v1", calLines[0].Key); // the same key scheme as held-out
        Assert.Equal(200, WorkbenchJson.ReadJsonl<BatchLine>(Path.Combine(repo.SpecDir, summary.PendingBatches[1])).Count);
        Assert.DoesNotContain(calLines, l => l.ItemId == "c1000");
        Assert.DoesNotContain(Batches(repo, "v1-alt"), l => l.ItemId.StartsWith('c'));
        Assert.Equal(summary.Splits["calibration"].Pending["v1"], WorkbenchJson.ReadJson<LabelSummary>(repo.Spec.LabelSummaryPath).Splits["calibration"].Pending["v1"]);
    }

    [Fact]
    public void CachedCalibrationKeysAreNeverExportedAgainAndCostCountsHeldOutOnly()
    {
        using var repo = FrontierRepo(calibration: 300, heldOut: 20, altSubset: 0);
        var held = Split(repo, "heldout");
        var cal = Split(repo, "calibration");
        FrontierStage.Run(repo.Spec, repo.Manifest, held, cal);
        var first = Batches(repo, "v1", "batch-calibration-*.jsonl");
        Answer(repo, first.Take(120), _ => "a");
        Answer(repo, Batches(repo, "v1", "batch-0*.jsonl"), _ => "b");

        var summary = FrontierStage.Run(repo.Spec, repo.Manifest, held, cal);
        Assert.Equal(120, summary.Splits["calibration"].Cached["v1"]);
        Assert.Equal(180, summary.Splits["calibration"].Pending["v1"]);
        Assert.Equal(0, summary.Splits["heldout"].Pending["v1"]);
        Assert.Equal(140, summary.Cached["v1"]);
        var again = Batches(repo, "v1", "batch-calibration-*.jsonl");
        Assert.Equal(180, again.Count);
        Assert.Empty(again.Select(b => b.Key).Intersect(first.Take(120).Select(b => b.Key)));

        // Cost is per decision served: the tally covers the 20 held-out answers, not the calibration ones.
        Assert.Equal(20, summary.Chars["v1"].Answers);
        Assert.Equal(held.Sum(i => (long)PromptTemplates.Render("v1", repo.Spec.Question, i.Text).Length), summary.Chars["v1"].InputChars);

        var output = new StringWriter();
        Assert.Equal(ExitCodes.Blocked, Pipeline.Label(repo.Spec, new PipelineOptions { Out = output }));
        Assert.Contains("calibration: v1 120 cached, 180 pending", output.ToString(), StringComparison.Ordinal);
        Answer(repo, again, _ => "c");
        Assert.Equal(ExitCodes.Ok, Pipeline.Label(repo.Spec, new PipelineOptions { Out = new StringWriter() }));
    }

    [Fact]
    public void GoldResolutionReturnsTheDatasetLabels()
    {
        using var repo = TestRepo.Create(heldOut: 4);
        var reference = ReferenceLabels.Load(repo.Spec);
        Assert.Equal(ReferenceKind.Gold, reference.Kind);
        Assert.Null(reference.Frontier);
        Assert.Equal("c", reference.LabelFor("h2", "c"));
        IReadOnlyList<MeasuredItem> records = [new MeasuredItem { ItemId = "h0", Gold = "a" }];
        Assert.Same(records, reference.Apply(repo.Spec.Question, "heldout", records));
    }

    [Fact]
    public void FrontierResolutionUsesTheCachedAnswerAfterOverridesAndAMissingLabelIsAnError()
    {
        using var repo = FrontierRepo(calibration: 4, heldOut: 4, altSubset: 0);
        var spec = repo.Spec;
        FrontierStage.Run(spec, repo.Manifest, Split(repo, "heldout"), Split(repo, "calibration"));
        Answer(repo, Batches(repo, "v1").Where(l => l.ItemId != "h3"), _ => "d");
        File.WriteAllLines(spec.OverridesPath, ["{\"item_id\":\"c1\",\"answer\":\"b\",\"by\":\"Rob\",\"date\":\"2026-09-27\",\"reason\":\"check\"}"]);

        var reference = ReferenceLabels.Load(spec);
        Assert.Equal(ReferenceKind.Frontier, reference.Kind);
        Assert.Equal("d", reference.LabelFor("h0", "a"));
        Assert.Equal("b", reference.LabelFor("c1", "b")); // the human override wins
        Assert.Equal("d", reference.LabelFor("c2", "c"));
        Assert.Null(reference.LabelFor("h3", "d"));
        Assert.Equal("c", reference.DatasetLabel("c2"));

        var q = spec.Question;
        var cal = Split(repo, "calibration").Select(i => new MeasuredItem { ItemId = i.Id, Gold = i.Label }.WithVector(q, [0.7, 0.1, 0.1, 0.1])).ToArray();
        var scored = reference.Apply(q, "calibration", cal);
        Assert.Equal(["d", "b", "d", "d"], scored.Select(r => r.Gold));
        Assert.All(scored, r => Assert.False(r.Correct)); // every answer is "a"; the frontier never said "a"
        Assert.Equal("a", cal[0].Gold); // the records themselves keep the dataset's label

        var held = Split(repo, "heldout").Select(i => new MeasuredItem { ItemId = i.Id, Gold = i.Label }.WithVector(q, [0.7, 0.1, 0.1, 0.1])).ToArray();
        var e = Assert.Throws<StageBlockedException>(() => reference.Apply(q, "heldout", held));
        Assert.Contains("1 of 4 heldout item(s) have no frontier reference label (for example h3)", e.Message, StringComparison.Ordinal);
        Assert.Contains("run 'tau label'", e.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<StageBlockedException>(() => reference.Require("heldout", Split(repo, "heldout")));
    }

    [Fact]
    public async Task MeasureStopsBeforeCallingTheEndpointWhenAFrontierLabelIsMissing()
    {
        using var repo = FrontierRepo(calibration: 5, heldOut: 5, altSubset: 0);
        var stub = new StubSystemOne();
        using var endpoint = new WorkbenchEndpoint(new Uri("http://stub:1"), stub, new RetryPolicy { MaxAttempts = 1 });
        var identity = await endpoint.IdentifyAsync(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<StageBlockedException>(() => MeasureStage.RunAsync(
            repo.Spec, "model-a", "calibration", Phases.Raw, Split(repo, "calibration"), endpoint, identity, MeasureTests.NoGpu, TestContext.Current.CancellationToken));
        Assert.DoesNotContain(stub.Calls, c => c.Text.StartsWith("calibration item", StringComparison.Ordinal));
    }

    [Fact]
    public void UnderAFrontierReferenceTheCascadeIsAgreementAndFrontierOnlyIsTotalByConstruction()
    {
        using var repo = FrontierRepo(calibration: 2, heldOut: 6, classes: 2, altSubset: 0);
        var spec = repo.Spec;
        var q = spec.Question;
        var held = Split(repo, "heldout"); // dataset labels: a, b, a, b, a, b

        // Frontier: h0 b, h1 b, h2 a, h3 a, h4 b (overridden to a), h5 a.
        FrontierStage.Run(spec, repo.Manifest, held, Split(repo, "calibration"));
        var frontierAnswers = new Dictionary<string, string> { ["h0"] = "b", ["h1"] = "b", ["h2"] = "a", ["h3"] = "a", ["h4"] = "b", ["h5"] = "a" };
        Answer(repo, Batches(repo, "v1", "batch-0*.jsonl"), l => frontierAnswers[l.ItemId]);
        File.WriteAllLines(spec.OverridesPath, ["{\"item_id\":\"h4\",\"answer\":\"a\",\"by\":\"Rob\",\"date\":\"2026-09-27\",\"reason\":\"fix\"}"]);
        var reference = ReferenceLabels.Load(spec);

        // Local: h0 b 0.9, h1 a 0.8, h2 a 0.7, h3 b 0.6, h4 a 0.55, h5 a 0.52.
        string[] local = ["b", "a", "a", "b", "a", "a"];
        double[] conf = [0.9, 0.8, 0.7, 0.6, 0.55, 0.52];
        var records = held.Select((item, i) =>
        {
            var v = new double[2];
            v[q.ClassIndex(local[i])!.Value] = conf[i];
            v[1 - q.ClassIndex(local[i])!.Value] = 1 - conf[i];
            return new MeasuredItem { ItemId = item.Id, Gold = item.Label, LatencyMs = 10 }.WithVector(q, v);
        }).ToArray();
        var threshold = new ThresholdResult
        {
            Model = "model-a", TargetError = 0.1, Source = "offline", Reachable = true, Tau = 0.65, HeldOutItems = 6,
            CalibrationCurve = [], HeldOutCurve = [], Note = "",
        };

        var c = CascadeStage.Simulate(spec, threshold, reference.Apply(q, "heldout", records), reference.Frontier!, records, "raw",
            reference.Frontier!.HeldOutChars("v1"), 200, 0.05);
        Assert.Equal(ReferenceKind.Frontier, c.Reference);
        Assert.Equal(CountedRate.Of(6, 4), c.LocalOnlyAccuracy);      // h0, h2, h4, h5 agree with the frontier
        Assert.Equal(CountedRate.Of(6, 6), c.FrontierOnlyAccuracy);   // by construction
        Assert.Contains("by construction", c.FrontierOnlyNote, StringComparison.Ordinal);
        Assert.Equal(3, c.KeptLocal);                                  // h0 (agrees), h1 (not), h2 (agrees)
        Assert.Equal(0, c.MissingFrontier);
        Assert.Equal(CountedRate.Of(6, 5), c.BlendedAccuracy);        // 2 of 3 kept + 3 of 3 served by the frontier
        Assert.Equal(1, c.OverridesUsed);
        Assert.Equal(0.5, c.Cost!.ShareEscalated!.Value, 10);

        // The same records under a gold reference: frontier-only is a real accuracy again, with no note.
        using var gold = TestRepo.Create(heldOut: 6, classes: 2, altSubset: 0);
        Directory.CreateDirectory(gold.Spec.FrontierDirectory);
        File.Copy(spec.CachePath, gold.Spec.CachePath);
        File.Copy(spec.OverridesPath, gold.Spec.OverridesPath);
        var g = CascadeStage.Simulate(gold.Spec, threshold, records, FrontierStage.LoadState(gold.Spec, gold.Manifest, Split(gold, "heldout")), records, "raw",
            new CharTally(6, 600, 6), 200, 0.05);
        Assert.Equal(CountedRate.Of(6, 3), g.FrontierOnlyAccuracy);   // h1, h2, h4 match the dataset's labels
        Assert.Null(g.FrontierOnlyNote);
    }
}
