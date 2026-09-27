using System.Net;
using System.Text;
using Tau.Calibration;
using Tau.Client;
using Tau.Workbench.Data;
using Tau.Workbench.Measure;

namespace Tau.Workbench.Tests;

public sealed class MeasureTests
{
    internal static readonly MeasureOptions NoGpu = new() { StartGpuSampler = () => (null, "no GPU in tests") };

    private static readonly RetryPolicy NoRetry = new() { MaxAttempts = 1 };

    private static async Task<MeasureSummary> Measure(TestRepo repo, StubSystemOne stub, string split = "heldout", string phase = Phases.Raw, MeasureOptions? options = null)
    {
        using var endpoint = new WorkbenchEndpoint(new Uri("http://stub:1"), stub, NoRetry);
        var identity = await endpoint.IdentifyAsync(TestContext.Current.CancellationToken);
        var items = PreparedDataset.LoadSplit(repo.Spec, repo.Manifest, split);
        return await MeasureStage.RunAsync(repo.Spec, "model-a", split, phase, items, endpoint, identity, options ?? NoGpu, TestContext.Current.CancellationToken);
    }

    private static int ItemNumber(string text) => int.Parse(text[(text.LastIndexOf(' ') + 1)..], System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task ChoiceProbabilitiesAreStoredInOptionKeyOrder()
    {
        using var repo = TestRepo.Create(heldOut: 8);
        var stub = new StubSystemOne { Probabilities = (_, _) => [0.1, 0.2, 0.3, 0.4] };
        var summary = await Measure(repo, stub);
        var records = WorkbenchJson.ReadJsonl<MeasuredItem>(repo.Spec.RunPath("model-a", "heldout", Phases.Raw));
        Assert.Equal(["a", "b", "c", "d"], records[0].Probabilities!.Keys);
        Assert.Equal([0.1, 0.2, 0.3, 0.4], records[0].Vector(repo.Spec.Question)!);
        Assert.Equal("d", records[0].Answer);
        Assert.Equal(0.4, records[0].ConfidenceMaxp);
        Assert.Equal(8, summary.Measured);
        Assert.Equal(0.25, summary.Metrics!.Accuracy); // gold cycles a..d; answer is always d
    }

    [Fact]
    public async Task ScoreVectorsFollowLevelIndices()
    {
        using var repo = TestRepo.Create(type: "score", classes: 3, heldOut: 3);
        var stub = new StubSystemOne { Probabilities = (_, _) => [0.2, 0.5, 0.3] };
        var summary = await Measure(repo, stub);
        var r = WorkbenchJson.ReadJsonl<MeasuredItem>(repo.Spec.RunPath("model-a", "heldout", Phases.Raw))[0];
        Assert.Equal([0.2, 0.5, 0.3], r.Vector(repo.Spec.Question)!);
        Assert.Equal("1", r.Answer);
        Assert.NotNull(summary.Metrics!.Mae);
        Assert.Equal((1 + 0 + 1) / 3.0, summary.Metrics.Mae!.Value, 12); // gold 0,1,2 vs answer 1
    }

    [Fact]
    public async Task NoulBecomesOneMinusPThenP()
    {
        using var repo = TestRepo.Create(type: "noul", heldOut: 2);
        var stub = new StubSystemOne { Probabilities = (_, _) => [0.2, 0.8] };
        await Measure(repo, stub);
        var r = WorkbenchJson.ReadJsonl<MeasuredItem>(repo.Spec.RunPath("model-a", "heldout", Phases.Raw))[0];
        Assert.Equal(0.2, r.Vector(repo.Spec.Question)![0], 12);
        Assert.Equal(0.8, r.Vector(repo.Spec.Question)![1], 12);
        Assert.Equal("true", r.Answer);
        Assert.True(r.Correct); // item 0 is gold "true"
    }

    [Fact]
    public async Task RawPhaseSendsTheRawHeaderAndCalibratedPhaseDoesNot()
    {
        using var repo = TestRepo.Create(heldOut: 5);
        var stub = new StubSystemOne { Calibrators = "model-a.choice" };
        await Measure(repo, stub);
        Assert.All(stub.Calls, c => Assert.True(c.Raw));
        var calibrated = new StubSystemOne { Calibrators = "model-a.choice" };
        var s = await Measure(repo, calibrated, phase: Phases.Calibrated);
        Assert.All(calibrated.Calls, c => Assert.False(c.Raw));
        Assert.Equal(["model-a.choice"], s.CalibratorsSeen);
    }

    [Theory]
    [InlineData(true, Precisions.Full)]
    [InlineData(false, Precisions.Rounded)]
    public async Task EveryRequestAsksForFullPrecisionAndTheSummaryRecordsWhetherItWasHonoured(bool honours, string expected)
    {
        using var repo = TestRepo.Create(heldOut: 5);
        var raw = new StubSystemOne { HonoursPrecision = honours };
        var rawSummary = await Measure(repo, raw);
        var calibrated = new StubSystemOne { HonoursPrecision = honours, Calibrators = "model-a.choice" };
        var calSummary = await Measure(repo, calibrated, phase: Phases.Calibrated);

        Assert.Equal(5, raw.PrecisionRequested.Count);
        Assert.All(raw.PrecisionRequested, Assert.True);
        Assert.All(calibrated.PrecisionRequested, Assert.True);
        Assert.Equal(expected, rawSummary.Precision);
        Assert.Equal(expected, calSummary.Precision);
        Assert.Equal(expected, WorkbenchJson.ReadJson<MeasureSummary>(repo.Spec.RunSummaryPath("model-a", "heldout", Phases.Raw)).Precision);
    }

    [Fact]
    public void PrecisionOfAPhaseAndItsCalibratorNote()
    {
        Assert.Equal(Precisions.Full, Precisions.Of(3, 3));
        Assert.Equal(Precisions.Rounded, Precisions.Of(0, 3));
        Assert.Equal(Precisions.Mixed, Precisions.Of(1, 3));
        Assert.Null(Precisions.CalibratorNote(Precisions.Full));
        Assert.Equal("Probabilities rounded to 4 dp by the endpoint; calibrators fitted on rounded values.", Precisions.CalibratorNote(Precisions.Rounded));
        Assert.Contains("calibrators fitted on rounded values", Precisions.CalibratorNote(null), StringComparison.Ordinal);
        Assert.Contains("fitted partly on rounded values", Precisions.CalibratorNote(Precisions.Mixed), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailuresAreRecordedExcludedAndCounted()
    {
        using var repo = TestRepo.Create(heldOut: 10);
        var stub = new StubSystemOne
        {
            Fail = text => ItemNumber(text) switch
            {
                3 => new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("boom") },
                5 => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity) { Content = new StringContent("{\"error\":{\"message\":\"bad\",\"details\":[{\"path\":\"$.state\",\"problem\":\"too long\"}]}}", Encoding.UTF8, "application/json") },
                7 => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{not json", Encoding.UTF8, "application/json") },
                _ => null,
            },
        };
        var summary = await Measure(repo, stub);
        Assert.Equal(10, summary.Items);
        Assert.Equal(7, summary.Measured);
        Assert.Equal(3, summary.Failures);
        Assert.Equal(7, summary.Metrics!.N);
        Assert.Equal(new Dictionary<string, int> { ["200"] = 1, ["422"] = 1, ["500"] = 1 }, summary.FailuresByStatus);
        Assert.Contains("3 of 10 items failed", summary.ExclusionNote, StringComparison.Ordinal);
        var records = WorkbenchJson.ReadJsonl<MeasuredItem>(repo.Spec.RunPath("model-a", "heldout", Phases.Raw));
        Assert.Equal(new MeasureError(500, "boom"), records[3].Error);
        Assert.Contains("too long", records[5].Error!.Body, StringComparison.Ordinal);
        Assert.Null(records[3].Probabilities);
    }

    [Fact]
    public async Task AnAnswerMissingAnOptionIsAFailureNotAGuess()
    {
        using var repo = TestRepo.Create(heldOut: 1);
        const string partial = """{"model":"model-a","answers":{"q":{"type":"choice","choice":"a","probabilities":{"a":0.6,"b":0.4},"confidence":0.6}},"usage":{"input_tokens":1,"output_tokens":0}}""";
        const string wrongType = """{"model":"model-a","answers":{"q":{"type":"noul","noul":0.3}},"usage":{"input_tokens":1,"output_tokens":0}}""";
        using var two = TestRepo.Create(heldOut: 2);
        var stub = new StubSystemOne
        {
            Fail = text => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ItemNumber(text) == 0 ? partial : wrongType, Encoding.UTF8, "application/json") },
        };
        var summary = await Measure(two, stub);
        Assert.Equal(2, summary.Failures);
        var records = WorkbenchJson.ReadJsonl<MeasuredItem>(two.Spec.RunPath("model-a", "heldout", Phases.Raw));
        Assert.Contains("no probability for 'c'", records[0].Error!.Body, StringComparison.Ordinal);
        Assert.Contains("of type 'noul'", records[1].Error!.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResultsAreInItemOrderWhateverTheCompletionOrder()
    {
        using var repo = TestRepo.Create(heldOut: 40);
        var stub = new StubSystemOne { MaxDelayMs = 15, Probabilities = (text, _) => ItemNumber(text) % 2 == 0 ? [0.7, 0.1, 0.1, 0.1] : [0.1, 0.7, 0.1, 0.1] };
        await Measure(repo, stub);
        var first = File.ReadAllText(repo.Spec.RunPath("model-a", "heldout", Phases.Raw));
        var ids = WorkbenchJson.ReadJsonl<MeasuredItem>(repo.Spec.RunPath("model-a", "heldout", Phases.Raw)).Select(r => r.ItemId).ToArray();
        Assert.Equal(Enumerable.Range(0, 40).Select(i => $"h{i}"), ids);
        await Measure(repo, new StubSystemOne { MaxDelayMs = 5, Probabilities = stub.Probabilities });
        var second = File.ReadAllText(repo.Spec.RunPath("model-a", "heldout", Phases.Raw));
        static string StripLatency(string s) => System.Text.RegularExpressions.Regex.Replace(s, "\"latency_ms\":[0-9.E+-]+", "");
        Assert.Equal(StripLatency(first), StripLatency(second));
    }

    [Fact]
    public async Task TauDiagnosticsAndIdentityAreRecorded()
    {
        using var repo = TestRepo.Create(heldOut: 4);
        var summary = await Measure(repo, new StubSystemOne { Truncate = true });
        Assert.True(summary.EndpointIdentity!.IsTau);
        Assert.Equal(StubSystemOne.Hash, summary.ModelHash);
        Assert.Equal([StubSystemOne.Hash], summary.ModelHashesSeen);
        Assert.Equal(4, summary.Truncated);
        Assert.Equal(3.25, summary.ModelMs!.P50);
        Assert.NotNull(summary.LatencyMs);
        Assert.Equal(4, summary.Concurrency);
        Assert.Equal("no GPU in tests", summary.GpuNote);
        Assert.Contains("max(p)", summary.ConfidenceNote, StringComparison.Ordinal);
        var r = WorkbenchJson.ReadJsonl<MeasuredItem>(repo.Spec.RunPath("model-a", "heldout", Phases.Raw))[0];
        Assert.Equal(3.25, r.ModelMs);
        Assert.True(r.Truncated);
    }

    [Fact]
    public async Task ANonTauEndpointWorksWithoutTauDiagnostics()
    {
        using var repo = TestRepo.Create(heldOut: 4);
        var summary = await Measure(repo, new StubSystemOne { IsTau = false });
        Assert.False(summary.EndpointIdentity!.IsTau);
        Assert.Equal(EndpointIdentity.NotTau, summary.EndpointIdentity.Description);
        Assert.Null(summary.ModelHash);
        Assert.Null(summary.ModelMs);
        Assert.Equal(4, summary.Measured);
    }

    [Fact]
    public async Task CalibratedPhaseWithNoCalibratorAppliedIsBlocked()
    {
        using var repo = TestRepo.Create(heldOut: 3);
        var e = await Assert.ThrowsAsync<StageBlockedException>(() => Measure(repo, new StubSystemOne { Calibrators = "none" }, phase: Phases.Calibrated));
        Assert.Contains("Tau:CalibratorsDirectory", e.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(repo.Spec.RunPath("model-a", "heldout", Phases.Calibrated)));
    }

    [Fact]
    public async Task ConnectionFailuresAreCapturedAsStatusZero()
    {
        using var repo = TestRepo.Create(heldOut: 2);
        var stub = new StubSystemOne { Fail = _ => throw new HttpRequestException("refused") };
        var summary = await Measure(repo, stub);
        Assert.Equal(2, summary.Failures);
        Assert.Null(summary.Metrics);
        Assert.Equal(new Dictionary<string, int> { ["0"] = 2 }, summary.FailuresByStatus);
    }

    [Fact]
    public void MetricsComeFromTauCalibrationAndBinsAddUpToTheEce()
    {
        var rng = new Random(7);
        var probs = Enumerable.Range(0, 500).Select(_ =>
        {
            var v = Enumerable.Range(0, 4).Select(_ => rng.NextDouble()).ToArray();
            var s = v.Sum();
            return v.Select(x => x / s).ToArray();
        }).ToArray();
        var gold = Enumerable.Range(0, 500).Select(_ => rng.Next(4)).ToArray();
        var m = MetricSet.Compute(probs, gold, ordinal: false)!;
        var conf = probs.Select(p => p.Max()).ToArray();
        var correct = probs.Select((p, i) => MetricSet.ArgMax(p) == gold[i]).ToArray();
        Assert.Equal(Metrics.ExpectedCalibrationError(conf, correct, 15), m.Ece);
        Assert.Equal(Metrics.BrierScore(probs, gold), m.Brier);
        Assert.Equal(Metrics.LogLoss(probs, gold), m.LogLoss);
        Assert.Equal(Metrics.Accuracy(probs, gold), m.Accuracy);
        Assert.Null(m.Mae);
        Assert.Equal(15, m.Reliability.Count);
        Assert.Equal(500, m.Reliability.Sum(b => b.Count));
        var fromBins = m.Reliability.Where(b => b.Count > 0).Sum(b => b.Count / 500.0 * Math.Abs(b.MeanConfidence!.Value - b.Accuracy!.Value));
        Assert.Equal(m.Ece, fromBins, 12);
        Assert.Null(MetricSet.Compute([], [], false));
    }

    [Fact]
    public void PercentilesInterpolate()
    {
        Assert.Equal(2.5, MetricSet.Percentile([1, 2, 3, 4], 50));
        Assert.Equal(4, MetricSet.Percentile([4], 95));
        Assert.Null(MetricSet.Percentile([], 50));
        Assert.Equal(0, MetricSet.ArgMax([0.5, 0.5]));
    }

    [Fact]
    public async Task GpuSamplerAveragesItsProbe()
    {
        int n = 0;
        var sampler = new GpuPowerSampler(_ => Task.FromResult<double?>(n++ % 2 == 0 ? 100 : 200), TimeSpan.FromMilliseconds(5), "Test GPU");
        await Task.Delay(80, TestContext.Current.CancellationToken);
        var power = await sampler.StopAsync();
        await sampler.DisposeAsync();
        Assert.NotNull(power);
        Assert.InRange(power!.MeanWatts, 100, 200);
        Assert.True(power.Samples >= 2);
        Assert.Equal("Test GPU", power.Gpu);
        Assert.Contains("whole-GPU", power.Note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GpuSamplerWithNoReadingsReturnsNull()
    {
        var sampler = new GpuPowerSampler(_ => Task.FromResult<double?>(null), TimeSpan.FromMilliseconds(5));
        await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.Null(await sampler.StopAsync());
        await sampler.DisposeAsync();
    }

    [Fact]
    public void RequestsCarryTheQuestionAndPinTheModel()
    {
        using var repo = TestRepo.Create();
        var r = MeasureStage.BuildRequest(repo.Spec.Question, "model-z", "hello");
        var json = System.Text.Json.JsonSerializer.Serialize(r, Tau.Contract.ContractJson.Options);
        Assert.True(Tau.Contract.ContractParser.TryParse(json, out var parsed, out var problems), string.Join("; ", problems.Select(p => p.Problem)));
        Assert.Equal("model-z", parsed!.Model);
        Assert.Equal("hello", (string)parsed.State!);
        Assert.Equal("q", Assert.Single(parsed.Questions).Key);
    }
}
