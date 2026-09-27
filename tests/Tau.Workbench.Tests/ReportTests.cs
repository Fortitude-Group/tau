using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Tau.Workbench.Baselines;
using Tau.Workbench.Calibrate;
using Tau.Workbench.Cascade;
using Tau.Workbench.Data;
using Tau.Workbench.Frontier;
using Tau.Workbench.Measure;
using Tau.Workbench.Report;
using Tau.Workbench.Threshold;

namespace Tau.Workbench.Tests;

public sealed class ReportTests
{
    internal static readonly ReportContext FixedContext = new()
    {
        Command = "tau report examples/demo/decision.yaml",
        Clock = () => new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero),
        Hardware = () => new HardwareInfo("Test GPU 12 GB", "999.1", "Test CPU", 16, "Test OS", ".NET 10"),
        Git = (_, _) => new GitInfo("0123456789abcdef0123456789abcdef01234567", false),
    };

    /// <summary>A repo where every stage has produced its artefacts.</summary>
    internal static TestRepo FullRun(int classes = 4, string type = "choice", bool synthetic = true, bool runtimePhase = true)
    {
        var repo = TestRepo.Create(type: type, classes: classes, calibration: 400, heldOut: 300, synthetic: synthetic,
            models: "[model-a, model-b]", baselines: "[classic, not-trained-yet]", targetError: 0.2, altSubset: 20);
        var spec = repo.Spec;
        Synthetic.WriteRawRuns(repo, "model-a", 2.5, seed: 1);
        Synthetic.WriteRawRuns(repo, "model-b", 1.2, seed: 2, modelHash: new string('b', 64));
        foreach (var m in spec.Models)
        {
            CalibrateStage.Run(spec, m, repo.Manifest, FixedContext.Clock);
        }

        if (runtimePhase)
        {
            foreach (var split in new[] { "calibration", "heldout" })
            {
                var offline = WorkbenchJson.ReadJsonl<MeasuredItem>(spec.RunPath("model-a", split, Phases.Offline))
                    .Select(r => r with { LatencyMs = 12, Calibrators = "model-a.choice" }).ToArray();
                Synthetic.WriteRun(spec, "model-a", split, Phases.Calibrated, offline, StubSystemOne.Hash);
            }
        }

        var held = PreparedDataset.LoadSplit(spec, repo.Manifest, "heldout");
        FrontierStage.Run(spec, repo.Manifest, held);
        var sb = new StringBuilder();
        foreach (var pv in new[] { "v1", "v1-alt" })
        {
            foreach (var file in Directory.EnumerateFiles(spec.PendingDirectory(pv)))
            {
                foreach (var line in WorkbenchJson.ReadJsonl<BatchLine>(file))
                {
                    int n = int.Parse(line.ItemId[1..], System.Globalization.CultureInfo.InvariantCulture);
                    var gold = held[n].Label;
                    var answer = n % 10 == 0 ? line.Allowed.First(a => a != gold) : gold; // 10% label noise
                    sb.Append(new JsonObject
                    {
                        ["key"] = line.Key, ["item_id"] = line.ItemId, ["prompt_version"] = pv, ["answer"] = answer,
                        ["model"] = "frontier-x", ["produced_by"] = "Claude Code session (subagent), not the API", ["date"] = "2026-09-27",
                    }.ToJsonString()).Append('\n');
                }
            }
        }

        File.AppendAllText(spec.CachePath, sb.ToString());
        FrontierStage.Run(spec, repo.Manifest, held);
        ThresholdStage.Run(spec);
        CascadeStage.Run(spec, repo.Manifest, held);

        var baseline = new StringBuilder();
        var rng = new Random(5);
        foreach (var (split, items) in new[] { ("calibration", PreparedDataset.LoadSplit(spec, repo.Manifest, "calibration")), ("heldout", held) })
        {
            foreach (var item in items)
            {
                var p = Synthetic.Draw(rng, spec.Question.Classes.Count, spec.Question.ClassIndex(item.Label)!.Value, 3);
                var probs = new JsonObject();
                for (int i = 0; i < p.Length; i++)
                {
                    probs[spec.Question.Classes[i]] = p[i];
                }

                baseline.Append(new JsonObject { ["id"] = item.Id, ["split"] = split, ["label"] = item.Label, ["probabilities"] = probs }.ToJsonString()).Append('\n');
            }
        }

        Directory.CreateDirectory(Path.Combine(repo.SpecDir, "baselines"));
        File.WriteAllText(spec.BaselinePath("classic"), baseline.ToString());
        return repo;
    }

    [Fact]
    public void TheReportIsSelfContainedAndHasEverySection()
    {
        using var repo = FullRun();
        var doc = ReportBuilder.Run(repo.Spec, repo.Manifest, FixedContext);
        var html = File.ReadAllText(repo.Spec.ReportHtmlPath);

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@import", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("url(", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<link", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("—", html, StringComparison.Ordinal); // no em dashes in prose
        foreach (var id in new[] { "dataset", "models", "reliability", "calibrators", "threshold", "cascade", "labels", "baselines", "confusion", "misses", "metadata" })
        {
            Assert.Contains($"<section id=\"{id}\">", html, StringComparison.Ordinal);
        }

        Assert.Contains("Synthetic data", html, StringComparison.Ordinal);
        Assert.Contains("Estimates, not measured bills", html, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(html, "aria-label=\"Reliability diagram").Count);
        Assert.Single(Regex.Matches(html, "aria-label=\"Trade-off curve"));
        Assert.Equal(2, Regex.Matches(html, "aria-label=\"Confusion matrix").Count);
        Assert.True(Regex.Matches(html, "class=\"caption\"").Count >= 11); // every figure and table explains itself
        Assert.Contains(StubSystemOne.Hash, html, StringComparison.Ordinal);
        Assert.Contains(repo.Manifest.Sha256, html, StringComparison.Ordinal);
        Assert.Contains("0123456789abcdef0123456789abcdef01234567 (clean, outputs excluded)", html, StringComparison.Ordinal);
        Assert.Contains("Test GPU 12 GB", html, StringComparison.Ordinal);
        Assert.Contains("tau report examples/demo/decision.yaml", html, StringComparison.Ordinal);
        Assert.Contains("2026-09-27T12:00:00Z", html, StringComparison.Ordinal);
        Assert.Contains("localhost:18093", html, StringComparison.Ordinal);
        Assert.Contains("max(p)", html, StringComparison.Ordinal);
        Assert.Contains("<details>", html, StringComparison.Ordinal); // table views for the charts

        Assert.Equal(2, doc.Models.Count);
        Assert.Equal(Phases.Calibrated, doc.Models[0].BestCalibratedSource);
        Assert.Equal(0, doc.Models[0].RuntimeVsOfflineMaxDiff);
        Assert.Equal(Phases.Offline, doc.Models[1].BestCalibratedSource);
        Assert.NotNull(doc.Frontier);
        Assert.Equal(0.1, doc.Frontier!.LabelNoise.Rate!.Value, 10);
        Assert.Contains(doc.Misses, m => m.StartsWith("Label noise", StringComparison.Ordinal));
        Assert.Equal("matrix", doc.Models[0].Confusion!.Kind);
        Assert.Equal(300, doc.Models[0].Confusion!.Matrix!.Sum(r => r.Sum()));
        Assert.Contains(doc.Baselines, b => b.Name == "not-trained-yet" && b.Missing is not null);
    }

    [Fact]
    public void EveryHeadlineNumberInTheHtmlIsInTheJson()
    {
        using var repo = FullRun();
        var doc = ReportBuilder.Run(repo.Spec, repo.Manifest, FixedContext);
        var json = File.ReadAllText(repo.Spec.ReportJsonPath);
        var html = File.ReadAllText(repo.Spec.ReportHtmlPath);
        var parsed = JsonNode.Parse(json)!;
        Assert.Equal(doc.Summary, (string)parsed["summary"]!);
        var ece = doc.Models[0].RawHeldOut!.Metrics!.Ece;
        Assert.Equal(ece, (double)parsed["models"]![0]!["raw_held_out"]!["metrics"]!["ece"]!, 12);
        Assert.Contains(Fmt.Num(ece), html, StringComparison.Ordinal);
        Assert.Contains(System.Net.WebUtility.HtmlEncode(doc.Summary), html, StringComparison.Ordinal);
        var cascade = doc.Cascades.First(c => c.Tau is not null);
        Assert.Contains(Fmt.Pct(cascade.ShareLocal), html, StringComparison.Ordinal);
        Assert.Equal(cascade.ShareLocal!.Value, (double)parsed["cascades"]!.AsArray().First(c => c!["tau"] is not null)!["share_local"]!, 12);
    }

    [Fact]
    public void RenderingIsDeterministic()
    {
        using var repo = FullRun();
        var a = HtmlReport.Render(ReportBuilder.Build(repo.Spec, repo.Manifest, FixedContext));
        var b = HtmlReport.Render(ReportBuilder.Build(repo.Spec, repo.Manifest, FixedContext));
        Assert.Equal(a, b);
    }

    [Fact]
    public void ManyClassesGetATopConfusionsTableInsteadOfAMatrix()
    {
        using var repo = FullRun(classes: 12, synthetic: false, runtimePhase: false);
        var doc = ReportBuilder.Run(repo.Spec, repo.Manifest, FixedContext);
        var html = File.ReadAllText(repo.Spec.ReportHtmlPath);
        Assert.Equal("top", doc.Models[0].Confusion!.Kind);
        Assert.InRange(doc.Models[0].Confusion!.Top!.Count, 1, 15);
        Assert.DoesNotContain("aria-label=\"Confusion matrix", html, StringComparison.Ordinal);
        Assert.Contains("most frequent confusions", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Synthetic data", html, StringComparison.Ordinal);
    }

    [Fact]
    public void AReportBeforeAnyMeasurementStillRendersAndSaysWhatIsMissing()
    {
        using var repo = TestRepo.Create();
        var doc = ReportBuilder.Run(repo.Spec, repo.Manifest, FixedContext);
        var html = File.ReadAllText(repo.Spec.ReportHtmlPath);
        Assert.Contains("has not run yet", html, StringComparison.Ordinal);
        Assert.Contains("No held-out measurement yet", html, StringComparison.Ordinal);
        Assert.All(doc.Models, m => Assert.Null(m.RawHeldOut));
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RefusedPoundsAreShownAsRefusals()
    {
        using var repo = FullRun();
        repo.WriteSpec(File.ReadAllText(repo.SpecPath).Replace("gbp_per_usd: 0.75", "gbp_per_usd: 0", StringComparison.Ordinal));
        CascadeStage.Run(repo.Spec, repo.Manifest, PreparedDataset.LoadSplit(repo.Spec, repo.Manifest, "heldout"));
        ReportBuilder.Run(repo.Spec, repo.Manifest, FixedContext);
        var html = File.ReadAllText(repo.Spec.ReportHtmlPath);
        Assert.Contains("No pound figures: the spec is missing pricing.gbp_per_usd", html, StringComparison.Ordinal);
        var firstCostTable = Regex.Match(html, "<section id=\"cascade\">.*?</section>", RegexOptions.Singleline).Value.Split("Basis:")[0];
        Assert.DoesNotContain("£", firstCostTable, StringComparison.Ordinal);
        Assert.Contains("$", firstCostTable, StringComparison.Ordinal); // dollar list prices stay visible
    }

    [Fact]
    public void MissesListBaselinesThatBeatTauAndWeakCalibration()
    {
        using var repo = FullRun();
        var doc = ReportBuilder.Build(repo.Spec, repo.Manifest, FixedContext);
        var baseline = doc.Baselines.First(b => b.Raw is not null);
        foreach (var m in doc.Models)
        {
            bool beats = baseline.Raw!.Accuracy > m.RawHeldOut!.Metrics!.Accuracy;
            Assert.Equal(beats, doc.Misses.Any(x => x.Contains($"beats {m.Model} on held-out accuracy", StringComparison.Ordinal)));
            bool weak = m.EceReduction < ReportBuilder.TargetEceReduction;
            Assert.Equal(weak, doc.Misses.Any(x => x.StartsWith($"Calibration cut {m.Model}'s", StringComparison.Ordinal)));
        }

        Assert.Contains(doc.Misses, x => x.StartsWith("Calibration helped", StringComparison.Ordinal));
    }

    [Fact]
    public void BaselinesAreScoredWithTheSameMetrics()
    {
        using var repo = FullRun();
        var b = BaselineStage.Score(repo.Spec, "classic");
        Assert.Equal(300, b.HeldOutItems);
        Assert.Equal(400, b.CalibrationItems);
        Assert.NotNull(b.Calibrated);
        Assert.NotNull(b.CalibrationMethod);
        Assert.Equal(64, b.Sha256!.Length);
        var missing = BaselineStage.Score(repo.Spec, "not-trained-yet");
        Assert.NotNull(missing.Missing);
        File.AppendAllText(repo.Spec.BaselinePath("classic"), "{\"id\":\"x\",\"split\":\"heldout\",\"label\":\"zz\",\"probabilities\":{}}\n");
        Assert.Contains("label 'zz'", Assert.Throws<WorkbenchException>(() => BaselineStage.Score(repo.Spec, "classic")).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ABaselineMissingAClassProbabilityIsRejected()
    {
        using var repo = TestRepo.Create();
        Directory.CreateDirectory(Path.Combine(repo.SpecDir, "baselines"));
        File.WriteAllText(repo.Spec.BaselinePath("x"), "{\"id\":\"h0\",\"split\":\"heldout\",\"label\":\"a\",\"probabilities\":{\"a\":0.5,\"b\":0.5}}\n");
        Assert.Contains("probability for 'c'", Assert.Throws<WorkbenchException>(() => BaselineStage.Score(repo.Spec, "x")).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FormattingIsCultureInvariant()
    {
        var saved = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal("12.3%", Fmt.Pct(0.1234));
            Assert.Equal("0.125", Fmt.Num(0.125));
            Assert.Equal("1,000", Fmt.Int(1000));
            Assert.Equal("£1,234", Fmt.Gbp(1234.4));
            Assert.Equal("£12.35", Fmt.Gbp(12.345));
            Assert.Equal("£0.0012", Fmt.Gbp(0.00123));
            Assert.Equal("$546", Fmt.Usd(546));
            Assert.Equal("n/a", Fmt.Pct(null));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = saved;
        }
    }
}
