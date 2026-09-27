using System.Text.Json.Nodes;
using Tau.Workbench.Data;
using Tau.Workbench.Frontier;

namespace Tau.Workbench.Tests;

public sealed class FrontierTests
{
    private static IReadOnlyList<DatasetItem> HeldOut(TestRepo repo) => PreparedDataset.LoadSplit(repo.Spec, repo.Manifest, "heldout");

    private static List<BatchLine> Batches(TestRepo repo, string pv) =>
        Directory.Exists(repo.Spec.PendingDirectory(pv))
            ? Directory.EnumerateFiles(repo.Spec.PendingDirectory(pv), "batch-*.jsonl").Order(StringComparer.Ordinal).SelectMany(WorkbenchJson.ReadJsonl<BatchLine>).ToList()
            : [];

    private static void Answer(TestRepo repo, IEnumerable<BatchLine> lines, Func<BatchLine, string> answer, Action<JsonObject>? tweak = null)
    {
        using var w = File.AppendText(repo.Spec.CachePath);
        foreach (var l in lines)
        {
            var o = new JsonObject
            {
                ["key"] = l.Key,
                ["item_id"] = l.ItemId,
                ["prompt_version"] = l.PromptVersion,
                ["answer"] = answer(l),
                ["model"] = "frontier-x",
                ["produced_by"] = "Claude Code session (subagent), not the API",
                ["date"] = "2026-09-27",
            };
            tweak?.Invoke(o);
            w.WriteLine(o.ToJsonString());
        }
    }

    [Fact]
    public void FirstRunExportsEverythingInBatchesOfAtMost200()
    {
        using var repo = TestRepo.Create(heldOut: 450, altSubset: 30);
        var summary = FrontierStage.Run(repo.Spec, repo.Manifest, HeldOut(repo));
        Assert.Equal(450, summary.Pending["v1"]);
        Assert.Equal(30, summary.Pending["v1-alt"]);
        Assert.Equal(480, summary.TotalPending);
        Assert.Equal(["frontier/pending/v1/batch-001.jsonl", "frontier/pending/v1/batch-002.jsonl", "frontier/pending/v1/batch-003.jsonl", "frontier/pending/v1-alt/batch-001.jsonl"], summary.PendingBatches);
        Assert.Equal(200, WorkbenchJson.ReadJsonl<BatchLine>(Path.Combine(repo.SpecDir, summary.PendingBatches[0])).Count);
        var v1 = Batches(repo, "v1");
        Assert.Equal(450, v1.Count);
        Assert.Equal("0123456789ab:h0:v1", v1[0].Key);
        Assert.Equal(["a", "b", "c", "d"], v1[0].Allowed);
        Assert.Contains("held-out item 0", v1[0].Prompt, StringComparison.Ordinal);
        Assert.True(File.Exists(repo.Spec.LabelSummaryPath));
    }

    [Fact]
    public void ExportNeverExceedsTheThousandItemCap()
    {
        using var repo = TestRepo.Create(heldOut: 1234, altSubset: 200);
        var summary = FrontierStage.Run(repo.Spec, repo.Manifest, HeldOut(repo));
        Assert.Equal(1234, summary.HeldOutItems);
        Assert.Equal(1000, summary.EligibleItems);
        var exported = Batches(repo, "v1").Concat(Batches(repo, "v1-alt")).Select(b => b.ItemId).Distinct().ToArray();
        Assert.Equal(1000, exported.Length);
        Assert.DoesNotContain("h1000", exported);
    }

    [Fact]
    public void CachedKeysAreNeverExportedAgain()
    {
        using var repo = TestRepo.Create(heldOut: 50, altSubset: 10);
        FrontierStage.Run(repo.Spec, repo.Manifest, HeldOut(repo));
        var first = Batches(repo, "v1");
        Answer(repo, first.Take(30), l => "a");
        var summary = FrontierStage.Run(repo.Spec, repo.Manifest, HeldOut(repo));
        Assert.Equal(30, summary.Cached["v1"]);
        Assert.Equal(20, summary.Pending["v1"]);
        var again = Batches(repo, "v1");
        Assert.Equal(20, again.Count);
        Assert.Empty(again.Select(b => b.Key).Intersect(first.Take(30).Select(b => b.Key)));

        Answer(repo, again, l => "b");
        Answer(repo, Batches(repo, "v1-alt"), l => "a");
        var done = FrontierStage.Run(repo.Spec, repo.Manifest, HeldOut(repo));
        Assert.Equal(0, done.TotalPending);
        Assert.Empty(Batches(repo, "v1"));
        Assert.Empty(done.PendingBatches);
    }

    [Theory]
    [InlineData("z", "not one of the allowed answers")]
    [InlineData("a, because", "not one of the allowed answers")]
    [InlineData("A", "not one of the allowed answers")]
    public void InvalidAnswersAreRejectedAndStayPending(string answer, string reason)
    {
        using var repo = TestRepo.Create(heldOut: 5, altSubset: 0);
        FrontierStage.Run(repo.Spec, repo.Manifest, HeldOut(repo));
        Answer(repo, Batches(repo, "v1").Take(1), _ => answer);
        var summary = FrontierStage.Run(repo.Spec, repo.Manifest, HeldOut(repo));
        Assert.Equal(5, summary.Pending["v1"]);
        var rejected = Assert.Single(summary.Rejected);
        Assert.Contains(reason, rejected.Reason, StringComparison.Ordinal);
        Assert.Equal(1, rejected.Line);
        Assert.Contains(Batches(repo, "v1"), b => b.ItemId == "h0");
    }

    [Fact]
    public void AnswersAreTrimmedBeforeValidation()
    {
        using var repo = TestRepo.Create(heldOut: 3, altSubset: 0);
        FrontierStage.Run(repo.Spec, repo.Manifest, HeldOut(repo));
        Answer(repo, Batches(repo, "v1"), _ => "  c\n");
        var state = FrontierStage.LoadState(repo.Spec, repo.Manifest, HeldOut(repo));
        Assert.Equal("c", state.Answer("h0", "v1")!.Answer);
    }

    [Theory]
    [InlineData("key", "wrong:h0:v1", "key does not match")]
    [InlineData("prompt_version", "v7", "not one this spec uses")]
    [InlineData("model", "", "provenance is incomplete")]
    [InlineData("date", null, "provenance is incomplete")]
    [InlineData("item_id", "h999", "key does not match")]
    public void MalformedCacheLinesAreRejected(string field, string? value, string reason)
    {
        using var repo = TestRepo.Create(heldOut: 3, altSubset: 0);
        FrontierStage.Run(repo.Spec, repo.Manifest, HeldOut(repo));
        Answer(repo, Batches(repo, "v1").Take(1), _ => "a", o =>
        {
            if (value is null)
            {
                o.Remove(field);
            }
            else
            {
                o[field] = value;
            }
        });
        var summary = FrontierStage.Run(repo.Spec, repo.Manifest, HeldOut(repo));
        Assert.Contains(reason, Assert.Single(summary.Rejected).Reason, StringComparison.Ordinal);
        Assert.Equal(0, summary.Cached["v1"]);
    }

    [Fact]
    public void GarbageLinesAreRejectedNotFatal()
    {
        using var repo = TestRepo.Create(heldOut: 3, altSubset: 0);
        Directory.CreateDirectory(repo.Spec.FrontierDirectory);
        File.WriteAllText(repo.Spec.CachePath, "not json\n\n[1]\n");
        var summary = FrontierStage.Run(repo.Spec, repo.Manifest, HeldOut(repo));
        Assert.Equal(2, summary.Rejected.Count);
        Assert.Equal(3, summary.Pending["v1"]);
    }

    [Fact]
    public void AnAltAnswerForAnItemOutsideTheSubsetIsRejected()
    {
        using var repo = TestRepo.Create(heldOut: 40, altSubset: 5);
        FrontierStage.Run(repo.Spec, repo.Manifest, HeldOut(repo));
        var outside = HeldOut(repo).Select(i => i.Id).Except(Batches(repo, "v1-alt").Select(b => b.ItemId)).First();
        Answer(repo, [new BatchLine($"0123456789ab:{outside}:v1-alt", outside, "v1-alt", "", [])], _ => "a");
        var summary = FrontierStage.Run(repo.Spec, repo.Manifest, HeldOut(repo));
        Assert.Contains("not in the exported set", Assert.Single(summary.Rejected).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateKeysKeepTheFirstAnswer()
    {
        using var repo = TestRepo.Create(heldOut: 2, altSubset: 0);
        FrontierStage.Run(repo.Spec, repo.Manifest, HeldOut(repo));
        var line = Batches(repo, "v1").Take(1).ToArray();
        Answer(repo, line, _ => "a");
        Answer(repo, line, _ => "b");
        var summary = FrontierStage.Run(repo.Spec, repo.Manifest, HeldOut(repo));
        Assert.Equal(1, summary.DuplicateCacheLines);
        Assert.Equal("a", FrontierStage.LoadState(repo.Spec, repo.Manifest, HeldOut(repo)).Answer("h0", "v1")!.Answer);
    }

    [Fact]
    public void AltSubsetIsDeterministicAndIndependentOfInputOrder()
    {
        var ids = Enumerable.Range(0, 1000).Select(i => $"item-{i}").ToArray();
        var a = FrontierStage.AltSubset(ids, 200);
        var b = FrontierStage.AltSubset(ids.Reverse().ToArray(), 200);
        Assert.Equal(200, a.Count);
        Assert.Equal(a.Order(StringComparer.Ordinal), b.Order(StringComparer.Ordinal));
        Assert.Equal(a, FrontierStage.AltSubset(ids, 200));
        Assert.NotEqual(ids.Take(200), a); // a hash-ordered choice, not simply the first 200
        Assert.Equal(ids.Where(a.Contains), a); // returned in input order
        Assert.Equal(5, FrontierStage.AltSubset(ids.Take(5).ToArray(), 200).Count);
        Assert.Empty(FrontierStage.AltSubset(ids, 0));
    }

    [Fact]
    public void LabelNoiseAgreementAndOverridesAreCounted()
    {
        using var repo = TestRepo.Create(heldOut: 10, altSubset: 4, classes: 2);
        FrontierStage.Run(repo.Spec, repo.Manifest, HeldOut(repo));
        // Gold alternates a, b. Answer "a" everywhere: wrong on the 5 "b" items.
        Answer(repo, Batches(repo, "v1"), _ => "a");
        var alt = Batches(repo, "v1-alt");
        Answer(repo, alt, l => l.ItemId == alt[0].ItemId ? "b" : "a");
        Directory.CreateDirectory(repo.Spec.FrontierDirectory);
        File.WriteAllLines(repo.Spec.OverridesPath,
        [
            "{\"item_id\":\"h1\",\"answer\":\"b\",\"by\":\"Rob\",\"date\":\"2026-09-27\",\"reason\":\"gold is right\"}",
            "{\"item_id\":\"h2\",\"answer\":\"q\",\"by\":\"Rob\",\"date\":\"2026-09-27\",\"reason\":\"typo\"}",
            "{\"item_id\":\"h3\",\"answer\":\"b\",\"date\":\"2026-09-27\"}",
        ]);
        var summary = FrontierStage.Run(repo.Spec, repo.Manifest, HeldOut(repo));
        Assert.Equal(CountedRate.Of(10, 5), summary.LabelNoise); // measured before overrides
        Assert.Equal(CountedRate.Of(4, 3), summary.PromptAgreement);
        Assert.Equal(1, summary.Overridden);
        Assert.Equal(2, summary.Rejected.Count(r => r.File == "overrides.jsonl"));
        var state = FrontierStage.LoadState(repo.Spec, repo.Manifest, HeldOut(repo));
        Assert.Equal("b", state.EffectiveAnswer("h1", "v1"));
        Assert.Equal("a", state.Answer("h1", "v1")!.Answer);
        Assert.Equal(["frontier-x"], summary.ProvenanceModels);
        Assert.Equal(["2026-09-27", "2026-09-27"], summary.ProvenanceDates);
    }

    [Fact]
    public void CharacterTalliesComeFromTheRecordOrThePrompt()
    {
        using var repo = TestRepo.Create(heldOut: 2, altSubset: 0);
        FrontierStage.Run(repo.Spec, repo.Manifest, HeldOut(repo));
        var lines = Batches(repo, "v1");
        Answer(repo, lines.Take(1), _ => "a", o => { o["input_chars"] = 1000; o["output_chars"] = 3; });
        Answer(repo, lines.Skip(1), _ => "b");
        var summary = FrontierStage.Run(repo.Spec, repo.Manifest, HeldOut(repo));
        Assert.Equal(new CharTally(2, 1000 + lines[1].Prompt.Length, 3 + 1), summary.Chars["v1"]);
    }

    [Fact]
    public void PromptWordingsDifferAndBothDemandOneAllowedAnswer()
    {
        using var repo = TestRepo.Create();
        var q = repo.Spec.Question;
        var v1 = PromptTemplates.Render("v1", q, "ITEM TEXT");
        var alt = PromptTemplates.Render("v1-alt", q, "ITEM TEXT");
        Assert.NotEqual(v1, alt);
        Assert.Contains("exactly one allowed answer and nothing else", v1, StringComparison.Ordinal);
        Assert.Contains("and nothing else", alt, StringComparison.Ordinal);
        Assert.True(v1.IndexOf("ITEM TEXT", StringComparison.Ordinal) > v1.IndexOf("- a: option a description", StringComparison.Ordinal));
        Assert.True(alt.IndexOf("ITEM TEXT", StringComparison.Ordinal) < alt.IndexOf("a = option a description", StringComparison.Ordinal));
        Assert.Throws<ArgumentException>(() => PromptTemplates.Render("v2", q, "x"));
    }

    [Fact]
    public void ScoreAndNoulPromptsListTheirAnswers()
    {
        using var score = TestRepo.Create(type: "score", classes: 3);
        var s = PromptTemplates.Render("v1", score.Spec.Question, "t");
        Assert.Contains("- 0: level 0", s, StringComparison.Ordinal);
        Assert.Contains("- 2: level 2", s, StringComparison.Ordinal);
        var sAlt = PromptTemplates.Render("v1-alt", score.Spec.Question, "t");
        Assert.True(sAlt.IndexOf("2 = level 2", StringComparison.Ordinal) < sAlt.IndexOf("0 = level 0", StringComparison.Ordinal));

        using var noul = TestRepo.Create(type: "noul");
        var n = PromptTemplates.Render("v1", noul.Spec.Question, "t");
        Assert.Contains("true or false", n, StringComparison.Ordinal);
        Assert.Contains("- true: it is", n, StringComparison.Ordinal);
    }

    [Fact]
    public void LabelExitsTwoWhilePendingAndZeroWhenDone()
    {
        using var repo = TestRepo.Create(heldOut: 3, altSubset: 1);
        var output = new StringWriter();
        Assert.Equal(ExitCodes.Blocked, Pipeline.Label(repo.Spec, new PipelineOptions { Out = output }));
        Assert.Contains("batch-001.jsonl", output.ToString(), StringComparison.Ordinal);
        Answer(repo, Batches(repo, "v1").Concat(Batches(repo, "v1-alt")), _ => "a");
        Assert.Equal(ExitCodes.Ok, Pipeline.Label(repo.Spec, new PipelineOptions { Out = new StringWriter() }));
    }
}
