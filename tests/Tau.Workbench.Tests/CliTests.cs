using System.Globalization;
using System.Text.Json.Nodes;
using Tau.Client;
using Tau.Workbench.Cli;
using Tau.Workbench.Frontier;
using Tau.Workbench.Measure;

namespace Tau.Workbench.Tests;

public sealed class CliTests
{
    private static async Task<(int Exit, string Out, string Err)> Tau(string[] args, Func<PipelineOptions, PipelineOptions>? configure = null)
    {
        var o = new StringWriter();
        var e = new StringWriter();
        int exit = await TauCli.RunAsync(args, o, e, configure);
        return (exit, o.ToString(), e.ToString());
    }

    /// <summary>A stub whose answers are right about 70% of the time and overconfident.</summary>
    private static StubSystemOne ModelStub(string calibrators = "none") => new()
    {
        Calibrators = calibrators,
        Probabilities = (text, _) =>
        {
            int n = int.Parse(text[(text.LastIndexOf(' ') + 1)..], CultureInfo.InvariantCulture);
            var rng = new Random(n * 2 + (text.StartsWith("held", StringComparison.Ordinal) ? 1 : 0));
            return Synthetic.Draw(rng, 4, n % 4, 2.5);
        },
    };

    private static Func<PipelineOptions, PipelineOptions> With(StubSystemOne stub) => o => o with
    {
        EndpointFactory = uri => new WorkbenchEndpoint(uri, stub, new RetryPolicy { MaxAttempts = 1 }),
        Measure = MeasureTests.NoGpu,
        Report = ReportTests.FixedContext with { Command = o.Report.Command },
    };

    private static void AnswerEverything(TestRepo repo)
    {
        var spec = repo.Spec;
        var held = Data.PreparedDataset.LoadSplit(spec, repo.Manifest, "heldout");
        using var w = File.AppendText(spec.CachePath);
        foreach (var pv in new[] { "v1", "v1-alt" })
        {
            foreach (var file in Directory.EnumerateFiles(spec.PendingDirectory(pv)))
            {
                foreach (var l in WorkbenchJson.ReadJsonl<BatchLine>(file))
                {
                    var gold = held.First(i => i.Id == l.ItemId).Label;
                    w.WriteLine(new JsonObject
                    {
                        ["key"] = l.Key, ["item_id"] = l.ItemId, ["prompt_version"] = pv, ["answer"] = gold,
                        ["model"] = "frontier-x", ["produced_by"] = "Claude Code session (subagent), not the API", ["date"] = "2026-09-27",
                    }.ToJsonString());
                }
            }
        }
    }

    [Fact]
    public async Task HelpAndVersionWork()
    {
        var (exit, output, _) = await Tau(["--help"]);
        Assert.Equal(0, exit);
        Assert.Contains("tau <stage> <decision.yaml>", output, StringComparison.Ordinal);
        foreach (var stage in new[] { "label", "measure", "calibrate", "threshold", "cascade", "report", "run" })
        {
            Assert.Contains(stage, output, StringComparison.Ordinal);
        }

        Assert.Equal(0, (await Tau(["--version"])).Exit);
        Assert.Equal(1, (await Tau([])).Exit);
    }

    [Theory]
    [InlineData(new[] { "bake", "x.yaml" }, "unknown stage")]
    [InlineData(new[] { "run" }, "missing the decision.yaml")]
    [InlineData(new[] { "run", "a.yaml", "b.yaml" }, "unexpected argument")]
    [InlineData(new[] { "run", "a.yaml", "--endpoint", "not a url" }, "not an absolute http(s) URL")]
    [InlineData(new[] { "run", "a.yaml", "--endpoint" }, "unknown or incomplete option")]
    [InlineData(new[] { "run", "a.yaml", "--phase", "calibrated" }, "only applies to 'measure'")]
    [InlineData(new[] { "measure", "a.yaml", "--phase", "cooked" }, "--phase must be")]
    [InlineData(new[] { "run", "a.yaml", "--verbose" }, "unknown or incomplete option")]
    public async Task BadArgumentsExitOneWithTheProblem(string[] args, string expected)
    {
        var (exit, _, err) = await Tau(args);
        Assert.Equal(1, exit);
        Assert.Contains(expected, err, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMissingSpecOrUnpreparedDataExitsOne()
    {
        var (exit, _, err) = await Tau(["label", Path.Combine(Path.GetTempPath(), "missing.yaml")]);
        Assert.Equal(1, exit);
        Assert.Contains("does not exist", err, StringComparison.Ordinal);

        using var repo = TestRepo.Create();
        File.Delete(Path.Combine(repo.DataDir, "heldout.jsonl"));
        var (exit2, _, err2) = await Tau(["label", repo.SpecPath]);
        Assert.Equal(1, exit2);
        Assert.Contains("run the sidecar data script", err2, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunDoesEverythingThenSkipsCurrentStages()
    {
        using var repo = TestRepo.Create(calibration: 240, heldOut: 120, altSubset: 10, targetError: 0.25);
        var stub = ModelStub();

        var first = await Tau(["run", repo.SpecPath], With(stub));
        Assert.Equal(2, first.Exit); // frontier answers pending
        Assert.Contains("answer(s) pending", first.Out, StringComparison.Ordinal);
        Assert.Contains("calibrated: skipped: the endpoint has not loaded calibrators", first.Out, StringComparison.Ordinal);
        Assert.True(File.Exists(repo.Spec.ReportHtmlPath));
        Assert.True(File.Exists(repo.Spec.CalibrationSummaryPath("model-a")));
        int callsAfterFirst = stub.Calls.Count;
        Assert.Equal(240 + 120 + 1, callsAfterFirst); // both splits raw, plus one calibrator probe

        AnswerEverything(repo);
        var second = await Tau(["run", repo.SpecPath], With(stub));
        Assert.Equal(0, second.Exit);
        Assert.Contains("model-a raw: skipped (current)", second.Out, StringComparison.Ordinal);
        Assert.Contains("calibrate: model-a: skipped (current)", second.Out, StringComparison.Ordinal);
        Assert.Equal(callsAfterFirst + 1, stub.Calls.Count); // only the probe
        var report = JsonNode.Parse(File.ReadAllText(repo.Spec.ReportJsonPath))!;
        Assert.Equal("tau run examples/demo/decision.yaml", (string)report["metadata"]!["command"]!);
        Assert.NotNull(report["cascades"]![0]!["cost"]);
    }

    [Fact]
    public async Task RunMeasuresTheCalibratedPhaseWhenTheRuntimeHasLoadedCalibrators()
    {
        using var repo = TestRepo.Create(calibration: 240, heldOut: 60, altSubset: 5);
        var stub = ModelStub(calibrators: "model-a.choice.3-5");
        var result = await Tau(["run", repo.SpecPath], With(stub));
        Assert.Equal(2, result.Exit);
        Assert.True(File.Exists(repo.Spec.RunPath("model-a", "heldout", Phases.Calibrated)));
        Assert.Contains(stub.Calls, c => !c.Raw);
    }

    [Fact]
    public async Task StagesCanRunOneByOne()
    {
        using var repo = TestRepo.Create(calibration: 240, heldOut: 60, altSubset: 5);
        var stub = ModelStub();
        Assert.Equal(2, (await Tau(["label", repo.SpecPath], With(stub))).Exit);
        AnswerEverything(repo);
        Assert.Equal(0, (await Tau(["label", repo.SpecPath], With(stub))).Exit);
        Assert.Equal(0, (await Tau(["measure", repo.SpecPath, "--endpoint", "http://other:9"], With(stub))).Exit);
        Assert.Equal(0, (await Tau(["calibrate", repo.SpecPath], With(stub))).Exit);
        var blocked = await Tau(["measure", repo.SpecPath, "--phase", "calibrated"], With(stub));
        Assert.Equal(2, blocked.Exit);
        Assert.Contains("Tau:CalibratorsDirectory", blocked.Err, StringComparison.Ordinal);
        Assert.Equal(0, (await Tau(["threshold", repo.SpecPath], With(stub))).Exit);
        Assert.Equal(0, (await Tau(["cascade", repo.SpecPath], With(stub))).Exit);
        var report = await Tau(["report", repo.SpecPath], With(stub));
        Assert.Equal(0, report.Exit);
        Assert.Contains("report.html", report.Out, StringComparison.Ordinal);
        var summary = WorkbenchJson.ReadJson<MeasureSummary>(repo.Spec.RunSummaryPath("model-a", "heldout", Phases.Raw));
        Assert.Equal("http://other:9/", summary.Endpoint);
    }

    [Fact]
    public async Task CascadeExitsTwoWhileEscalatedItemsLackFrontierAnswers()
    {
        using var repo = TestRepo.Create(calibration: 240, heldOut: 60, altSubset: 5, targetError: 0.25);
        var stub = ModelStub();
        await Tau(["measure", repo.SpecPath], With(stub));
        await Tau(["calibrate", repo.SpecPath], With(stub));
        await Tau(["threshold", repo.SpecPath], With(stub));
        var r = await Tau(["cascade", repo.SpecPath], With(stub));
        var cascade = WorkbenchJson.ReadJson<List<Cascade.CascadeResult>>(repo.Spec.CascadePath)[0];
        Assert.Equal(cascade.MissingFrontier > 0 ? 2 : 0, r.Exit);
    }

    [Fact]
    public async Task CalibrateAgainstANonTauEndpointFailsClearly()
    {
        using var repo = TestRepo.Create(calibration: 30, heldOut: 10);
        var stub = new StubSystemOne { IsTau = false };
        Assert.Equal(0, (await Tau(["measure", repo.SpecPath], With(stub))).Exit);
        var r = await Tau(["calibrate", repo.SpecPath], With(stub));
        Assert.Equal(1, r.Exit);
        Assert.Contains("not a Tau endpoint", r.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void StalenessComparesOldestOutputWithNewestInput()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tau-stale-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            string input = Path.Combine(dir, "in"), output = Path.Combine(dir, "out");
            File.WriteAllText(input, "i");
            Assert.False(Staleness.IsCurrent([input], [output]));
            File.WriteAllText(output, "o");
            File.SetLastWriteTimeUtc(input, DateTime.UtcNow.AddMinutes(-5));
            Assert.True(Staleness.IsCurrent([input, Path.Combine(dir, "absent")], [output]));
            File.SetLastWriteTimeUtc(input, DateTime.UtcNow.AddMinutes(5));
            Assert.False(Staleness.IsCurrent([input], [output]));
            Assert.False(Staleness.IsCurrent([input], []));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
