using System.Text.Json.Nodes;
using Tau.Workbench.Baselines;
using Tau.Workbench.Cascade;
using Tau.Workbench.Data;
using Tau.Workbench.Frontier;
using Tau.Workbench.Measure;
using Tau.Workbench.Report;

namespace Tau.Workbench.Tests;

/// <summary>Which cascade the summary quotes, and the baseline put through the same threshold and cascade.</summary>
public sealed class CascadeReportTests
{
    /// <summary>A cascade with the given share kept local and blended rate, over 1,000 items.</summary>
    private static CascadeResult Crafted(string model, double share, double blended, double? tau = 0.9)
    {
        int kept = (int)Math.Round(share * 1000);
        return new CascadeResult
        {
            Model = model,
            Source = Phases.Offline,
            Tau = tau,
            Items = 1000,
            ExcludedFailures = 0,
            KeptLocal = kept,
            Escalated = 1000 - kept,
            ShareLocal = share,
            ShareEscalated = 1 - share,
            LocalOnlyAccuracy = CountedRate.Of(1000, 700),
            FrontierOnlyAccuracy = CountedRate.Of(1000, 942),
            BlendedAccuracy = CountedRate.Of(1000, (int)Math.Round(blended * 1000)),
        };
    }

    [Fact]
    public void TheSummaryQuotesTheCascadeThatKeepsTheMostLocal()
    {
        using var repo = ReportTests.FullRun();
        WorkbenchJson.WriteJson(repo.Spec.CascadePath, new List<CascadeResult> { Crafted("model-a", 0.045, 0.942), Crafted("model-b", 0.736, 0.932) });
        var doc = ReportBuilder.Build(repo.Spec, repo.Manifest, ReportTests.FixedContext);
        Assert.Contains(
            "At a 20.0% target error, model-b keeps 73.6% of decisions local, the most of any model (model-a, the least, keeps 4.5%); the cascade is 93.2% accurate against 94.2% for frontier-x alone.",
            doc.Summary, StringComparison.Ordinal);
        Assert.Contains("Out of the box, model-a", doc.Summary, StringComparison.Ordinal); // the ECE sentence still quotes the first model
    }

    [Fact]
    public void ATieAtTheDisplayedShareIsCalledATie()
    {
        using var repo = ReportTests.FullRun();
        WorkbenchJson.WriteJson(repo.Spec.CascadePath, new List<CascadeResult> { Crafted("model-a", 0.001, 0.9), Crafted("model-b", 0.001, 0.8) });
        var doc = ReportBuilder.Build(repo.Spec, repo.Manifest, ReportTests.FixedContext);
        Assert.Contains("model-a keeps 0.1% of decisions local (2 models tie at that share); ", doc.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("the most of any model", doc.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void WithOneCascadeTheSummaryNamesNoLeast()
    {
        using var repo = ReportTests.FullRun();
        WorkbenchJson.WriteJson(repo.Spec.CascadePath, new List<CascadeResult> { Crafted("model-a", 0.3, 0.9), Crafted("model-b", 0.9, 0.9, tau: null) });
        var doc = ReportBuilder.Build(repo.Spec, repo.Manifest, ReportTests.FixedContext);
        Assert.Contains("model-a keeps 30.0% of decisions local; ", doc.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("the least", doc.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBestCascadeBreaksTiesOnBlendedRateThenSpecOrder()
    {
        using var repo = TestRepo.Create(models: "[model-a, model-b, model-c]");
        var spec = repo.Spec;
        Assert.Equal("model-b", ReportBuilder.BestCascade(spec, [Crafted("model-a", 0.5, 0.90), Crafted("model-b", 0.5, 0.95), Crafted("model-c", 0.99, 0.99, tau: null)])!.Model);
        Assert.Equal("model-a", ReportBuilder.BestCascade(spec, [Crafted("model-b", 0.5, 0.9), Crafted("model-a", 0.5, 0.9)])!.Model);
        Assert.Null(ReportBuilder.BestCascade(spec, [Crafted("model-a", 0.5, 0.9, tau: null)]));
    }

    [Fact]
    public void ABaselineThresholdIsChosenOnItsCalibrationLinesOnly()
    {
        using var repo = ReportTests.FullRun();
        var spec = repo.Spec;
        var before = BaselineStage.Score(spec, "classic").Threshold!;
        Assert.Equal("classic", before.Model);
        Assert.Equal(BaselineStage.CalibratedSource, before.Source);
        Assert.True(before.Reachable);
        Assert.Equal(300, before.HeldOutItems);

        // Make every held-out line confidently wrong: τ and the calibration curve must not move, only the held-out result.
        var path = spec.BaselinePath("classic");
        var classes = spec.Question.Classes;
        File.WriteAllLines(path, File.ReadAllLines(path).Select(line =>
        {
            var obj = JsonNode.Parse(line)!.AsObject();
            if ((string)obj["split"]! != "heldout")
            {
                return line;
            }

            var gold = (string)obj["label"]!;
            var wrong = classes.First(k => k != gold);
            var probs = new JsonObject();
            foreach (var k in classes)
            {
                probs[k] = k == wrong ? 0.97 : 0.03 / (classes.Count - 1);
            }

            obj["probabilities"] = probs;
            return obj.ToJsonString();
        }));
        var after = BaselineStage.Score(spec, "classic").Threshold!;
        Assert.Equal(before.Tau, after.Tau);
        Assert.Equal(before.CalibrationAcceptRate, after.CalibrationAcceptRate);
        Assert.Equal(before.CalibrationCurve, after.CalibrationCurve);
        Assert.NotEqual(before.HeldOutCurve, after.HeldOutCurve);

        // With no calibration lines there is no calibrated view, so the raw probabilities are used and no τ can be chosen.
        File.WriteAllLines(spec.BaselinePath("held-only"), File.ReadAllLines(path).Where(l => l.Contains("\"split\":\"heldout\"", StringComparison.Ordinal)));
        var raw = BaselineStage.Score(spec, "held-only").Threshold!;
        Assert.Equal(BaselineStage.RawSource, raw.Source);
        Assert.False(raw.Reachable);
    }

    [Fact]
    public void TheBaselineCascadeUsesTheSameRuleAndOmitsLocalEnergy()
    {
        using var repo = ReportTests.FullRun();
        var spec = repo.Spec;
        var frontier = FrontierStage.LoadState(spec, repo.Manifest, PreparedDataset.LoadSplit(spec, repo.Manifest, "heldout"));
        var b = BaselineStage.Score(spec, "classic", null, frontier);
        var t = b.Threshold!;
        var c = b.Cascade!;
        Assert.Equal("classic", c.Model);
        Assert.Equal(BaselineStage.CalibratedSource, c.Source);
        Assert.Equal(t.Tau, c.Tau);
        Assert.Equal(t.HeldOutAccepted!.Value, c.KeptLocal); // confidence ≥ τ on the held-out lines, as for the models
        Assert.Equal(t.HeldOutAcceptRate!.Value, c.ShareLocal!.Value, 12);
        Assert.Equal(300, c.Items);
        Assert.Equal(0, c.MissingFrontier);
        Assert.Equal(300, c.BlendedAccuracy!.N);
        Assert.Null(c.LocalLatencyP50Ms);
        Assert.Null(c.LatencySource);

        var cost = c.Cost!;
        Assert.Null(cost.GpuMeanWatts);
        Assert.Null(cost.LocalKwhPerDecision);
        Assert.Null(cost.LocalGbpPerDecision);
        Assert.Null(cost.CascadeGbpRefused);
        var headline = cost.Rows[0];
        Assert.Equal(c.ShareEscalated!.Value * headline.FrontierOnlyGbpPerMillion!.Value, headline.CascadeGbpPerMillion!.Value, 6);
        Assert.Contains(cost.Basis, x => x.StartsWith("Local energy is not included", StringComparison.Ordinal));

        // The report simulates the same cascade and labels it wherever it sits beside the Tau models.
        var doc = ReportBuilder.Run(spec, repo.Manifest, ReportTests.FixedContext);
        var reported = doc.Baselines.Single(x => x.Name == "classic");
        Assert.Equal(c.ShareLocal, reported.Cascade!.ShareLocal);
        Assert.Equal(c.BlendedAccuracy, reported.Cascade.BlendedAccuracy);
        Assert.Equal(t.Tau, reported.Threshold!.Tau);
        Assert.Null(doc.Baselines.Single(x => x.Name == "not-trained-yet").Cascade);
        var html = File.ReadAllText(spec.ReportHtmlPath);
        Assert.Contains("classic (baseline, not served through Tau: local latency and energy not measured)", html, StringComparison.Ordinal);
        Assert.Contains("Cost for classic (baseline, not served through Tau", html, StringComparison.Ordinal);
        Assert.Contains("<td>classic (baseline)</td><td>baseline-calibrated</td>", html, StringComparison.Ordinal);
        Assert.Contains("line s3 dashed", html, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissIsListedWhenABaselineKeepsMoreLocalAtLeastAsWell()
    {
        using var repo = ReportTests.FullRun();
        var spec = repo.Spec;
        WorkbenchJson.WriteJson(spec.CascadePath, new List<CascadeResult> { Crafted("model-a", 0.01, 0.5), Crafted("model-b", 0.005, 0.4) });
        var doc = ReportBuilder.Build(spec, repo.Manifest, ReportTests.FixedContext);
        var bc = doc.Baselines.Single(b => b.Name == "classic").Cascade!;
        Assert.True(bc.ShareLocal > 0.01 && bc.BlendedAccuracy!.Rate >= 0.5); // the baseline beats model-a here
        Assert.Contains(
            $"Baseline classic keeps {Fmt.Pct(bc.ShareLocal)} local against model-a's 1.0%, at {Fmt.Pct(bc.BlendedAccuracy!.Rate)} against 50.0% blended accuracy.",
            doc.Misses);
        Assert.Contains("The classic baseline classic (not served through Tau) does better", doc.Summary, StringComparison.Ordinal);

        // More local but less accurate is a trade-off, not a win; neither is less local.
        foreach (var crafted in new[] { Crafted("model-a", 0.001, 0.9999), Crafted("model-a", 0.999, 0.1) })
        {
            WorkbenchJson.WriteJson(spec.CascadePath, new List<CascadeResult> { crafted });
            doc = ReportBuilder.Build(spec, repo.Manifest, ReportTests.FixedContext);
            Assert.DoesNotContain(doc.Misses, m => m.StartsWith("Baseline classic keeps", StringComparison.Ordinal));
            Assert.DoesNotContain("does better", doc.Summary, StringComparison.Ordinal);
        }
    }
}
