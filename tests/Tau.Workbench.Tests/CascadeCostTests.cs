using System.Text.Json.Nodes;
using Tau.Workbench.Cascade;
using Tau.Workbench.Data;
using Tau.Workbench.Frontier;
using Tau.Workbench.Measure;
using Tau.Workbench.Spec;
using Tau.Workbench.Threshold;

namespace Tau.Workbench.Tests;

public sealed class CascadeCostTests
{
    private static PricingSpec Pricing(double? gbp = 0.75, double? kwh = 0.25) => new(
        "2026-09-27", "example pricing page",
        [new PriceRow("frontier-x", 4, 20), new PriceRow("frontier-x-batch", 2, 10), new PriceRow("cheap-y", 1, 5)],
        "frontier-x", gbp, "test rate", kwh, "test tariff", 1.3);

    [Fact]
    public void CostMatchesHandComputedValues()
    {
        // 10 answers, 4,000 input chars and 40 output chars in total: 400 and 4 chars per decision.
        // Tokens: 400 / 4 × 1.3 = 130 input, 4 / 4 × 1.3 = 1.3 output.
        // Headline USD per decision: 130 × 4 / 1e6 + 1.3 × 20 / 1e6 = 0.000546.
        // Local: 200 W × 0.05 s = 10 J = 10 / 3.6e6 kWh; × £0.25 = £6.9444e-7 per decision.
        var cost = CostModel.Estimate(Pricing(), new CharTally(10, 4000, 40), shareEscalated: 0.3, gpuMeanWatts: 200, secondsPerDecision: 0.05);
        Assert.Equal(130, cost.InputTokensPerDecision, 10);
        Assert.Equal(1.3, cost.OutputTokensPerDecision, 10);
        var headline = cost.Rows[0];
        Assert.Equal("headline", headline.Kind);
        Assert.Equal(0.000546, headline.FrontierUsdPerDecision, 12);
        Assert.Equal(546, headline.FrontierOnlyUsdPerMillion, 6);
        Assert.Equal(0.546, headline.FrontierOnlyUsdPer1k, 9);
        Assert.Equal(546 * 0.75, headline.FrontierOnlyGbpPerMillion!.Value, 6);
        double local = 200 * 0.05 / 3_600_000 * 0.25;
        Assert.Equal(local, cost.LocalGbpPerDecision!.Value, 15);
        double cascade = local + (0.3 * 0.000546 * 0.75);
        Assert.Equal(cascade * 1e6, headline.CascadeGbpPerMillion!.Value, 6);
        Assert.Equal(cascade * 1e3, headline.CascadeGbpPer1k!.Value, 9);
        Assert.Equal((0.000546 * 0.75 - cascade) * 1e6, headline.SavingGbpPerMillion!.Value, 6);
        Assert.True(cost.Estimate);
        Assert.Equal("same-model-rate", cost.Rows[1].Kind);
        Assert.Equal("what-if", cost.Rows[2].Kind);
        Assert.Contains("accuracy was not measured", cost.Rows[2].Label, StringComparison.Ordinal);
        Assert.Contains(cost.Basis, b => b.Contains("not the API", StringComparison.Ordinal));
        Assert.Contains(cost.Basis, b => b.Contains("depreciation are excluded", StringComparison.Ordinal));
        Assert.Null(cost.GbpRefused);
        Assert.Null(cost.CascadeGbpRefused);
    }

    [Theory]
    [InlineData(null, 0.25, "pricing.gbp_per_usd")]
    [InlineData(0.75, null, "pricing.electricity_gbp_per_kwh")]
    [InlineData(null, null, "pricing.gbp_per_usd (the USD to GBP rate) and pricing.electricity_gbp_per_kwh")]
    public void PoundsAreRefusedWhenARateIsMissing(double? gbp, double? kwh, string named)
    {
        var cost = CostModel.Estimate(Pricing(gbp, kwh), new CharTally(10, 4000, 40), 0.3, 200, 0.05);
        Assert.Contains(named, cost.GbpRefused, StringComparison.Ordinal);
        Assert.All(cost.Rows, r =>
        {
            Assert.Null(r.FrontierOnlyGbpPerMillion);
            Assert.Null(r.CascadeGbpPerMillion);
            Assert.True(r.FrontierOnlyUsdPerMillion > 0); // list prices in dollars stay visible
        });
        Assert.Null(cost.LocalGbpPerDecision);
    }

    [Fact]
    public void WithoutGpuPowerOnlyTheCascadePoundsAreRefused()
    {
        var cost = CostModel.Estimate(Pricing(), new CharTally(10, 4000, 40), 0.3, null, 0.05);
        Assert.NotNull(cost.Rows[0].FrontierOnlyGbpPerMillion);
        Assert.Null(cost.Rows[0].CascadeGbpPerMillion);
        Assert.Contains("GPU power was not sampled", cost.CascadeGbpRefused, StringComparison.Ordinal);
    }

    [Fact]
    public void NoCascadeMeansNoCascadePounds()
    {
        var cost = CostModel.Estimate(Pricing(), new CharTally(10, 4000, 40), null, 200, 0.05);
        Assert.Contains("no threshold met the target", cost.CascadeGbpRefused, StringComparison.Ordinal);
        Assert.NotNull(cost.Rows[0].FrontierOnlyGbpPerMillion);
    }

    [Fact]
    public void NoCachedAnswersIsAnError() =>
        Assert.Throws<WorkbenchException>(() => CostModel.Estimate(Pricing(), new CharTally(0, 0, 0), 0.3, 200, 0.05));

    [Fact]
    public void CascadeBlendsLocalAndFrontierAnswersByHand()
    {
        using var repo = TestRepo.Create(heldOut: 6, classes: 2, altSubset: 0);
        var spec = repo.Spec;
        var held = PreparedDataset.LoadSplit(spec, repo.Manifest, "heldout"); // gold: a, b, a, b, a, b
        // Local: confidences 0.9 (right), 0.8 (wrong), 0.7 (right), 0.6 (wrong), 0.55 (wrong), 0.52 (right)
        double[] conf = [0.9, 0.8, 0.7, 0.6, 0.55, 0.52];
        bool[] right = [true, false, true, false, false, true];
        var records = held.Select((item, i) =>
        {
            int gold = spec.Question.ClassIndex(item.Label)!.Value;
            int answer = right[i] ? gold : 1 - gold;
            var v = new double[2];
            v[answer] = conf[i];
            v[1 - answer] = 1 - conf[i];
            return new MeasuredItem { ItemId = item.Id, Gold = item.Label, LatencyMs = 10 * (i + 1) }.WithVector(spec.Question, v);
        }).ToList();
        records.Add(new MeasuredItem { ItemId = "failed", Gold = "a", Error = new MeasureError(500, "x") });

        // Frontier: right on h3 and h4, wrong on h0; h5 has no answer; h4 is a human override.
        Directory.CreateDirectory(spec.FrontierDirectory);
        string Line(string id, string answer) => new JsonObject
        {
            ["key"] = $"0123456789ab:{id}:v1", ["item_id"] = id, ["prompt_version"] = "v1", ["answer"] = answer,
            ["model"] = "frontier-x", ["produced_by"] = "session", ["date"] = "2026-09-27", ["input_chars"] = 400, ["output_chars"] = 1,
        }.ToJsonString();
        File.WriteAllLines(spec.CachePath, [Line("h0", "b"), Line("h3", "b"), Line("h4", "b")]);
        File.WriteAllLines(spec.OverridesPath, ["{\"item_id\":\"h4\",\"answer\":\"a\",\"by\":\"Rob\",\"date\":\"2026-09-27\",\"reason\":\"fix\"}"]);
        var frontier = FrontierStage.LoadState(spec, repo.Manifest, held);

        var threshold = new ThresholdResult
        {
            Model = "model-a", TargetError = 0.1, Source = "offline", Reachable = true, Tau = 0.65, HeldOutItems = 6,
            CalibrationCurve = [], HeldOutCurve = [], Note = "",
        };
        var chars = new CharTally(3, 1200, 3);
        var c = CascadeStage.Simulate(spec, threshold, records, frontier, records, "raw", chars, 200, 0.05);

        Assert.Equal(6, c.Items);
        Assert.Equal(1, c.ExcludedFailures);
        Assert.Equal(3, c.KeptLocal);          // 0.9, 0.8, 0.7
        Assert.Equal(3, c.Escalated);          // h3, h4, h5
        Assert.Equal(1, c.MissingFrontier);    // h5
        Assert.Equal(0.5, c.ShareLocal!.Value, 10);
        Assert.Equal(CountedRate.Of(6, 3), c.LocalOnlyAccuracy);
        Assert.Equal(CountedRate.Of(3, 2), c.FrontierOnlyAccuracy); // h0 wrong, h3 right, h4 right (override)
        Assert.Equal(CountedRate.Of(5, 4), c.BlendedAccuracy);      // local 2 of 3, frontier 2 of 2
        Assert.Equal(1, c.OverridesUsed);
        Assert.Equal(35, c.LocalLatencyP50Ms!.Value, 10);           // median of 10..60
        Assert.Equal("raw", c.LatencySource);
        Assert.Contains("not measured", c.FrontierLatencyNote, StringComparison.Ordinal);
        Assert.Equal(0.5, c.Cost!.ShareEscalated!.Value, 10);
        Assert.Equal(400 / 4.0 * 1.3, c.Cost.InputTokensPerDecision, 10);
    }

    [Fact]
    public void WithoutAThresholdTheCascadeIsNotSimulatedButFrontierCostIsKept()
    {
        using var repo = TestRepo.Create(heldOut: 2, altSubset: 0);
        var held = PreparedDataset.LoadSplit(repo.Spec, repo.Manifest, "heldout");
        var frontier = FrontierStage.LoadState(repo.Spec, repo.Manifest, held);
        var t = new ThresholdResult { Model = "m", TargetError = 0.01, Source = "raw", Reachable = false, HeldOutItems = 0, CalibrationCurve = [], HeldOutCurve = [], Note = "" };
        var c = CascadeStage.Simulate(repo.Spec, t, [], frontier, null, null, new CharTally(1, 400, 1), null, null);
        Assert.Contains("No cascade", c.NotSimulated, StringComparison.Ordinal);
        Assert.Null(c.BlendedAccuracy);
        Assert.NotNull(c.Cost);
        var none = CascadeStage.Simulate(repo.Spec, t, [], frontier, null, null, new CharTally(0, 0, 0), null, null);
        Assert.Null(none.Cost);
        Assert.Contains("No frontier answer is cached", none.CostUnavailable, StringComparison.Ordinal);
    }
}
