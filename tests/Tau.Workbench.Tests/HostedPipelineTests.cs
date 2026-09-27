using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using Tau.Calibration;
using Tau.Client;
using Tau.Workbench.Calibrate;
using Tau.Workbench.Cli;
using Tau.Workbench.Data;
using Tau.Workbench.Measure;
using Tau.Workbench.Spec;

namespace Tau.Workbench.Tests;

/// <summary>
/// A hosted endpoint measured end to end, against an in-process stub only: nothing here ever reaches a real endpoint,
/// and the "key" is a made-up string handed in through a fake environment reader.
/// </summary>
public sealed class HostedPipelineTests
{
    private const string Key = "sk-test-DO-NOT-LEAK-7f3a9c11";

    private static readonly RetryPolicy FastHosted = WorkbenchEndpoint.HostedRetryPolicy with { DelayAsync = static (_, _) => Task.CompletedTask };

    private static string? UserScopeOnly(string name, EnvironmentVariableTarget target) =>
        name == HostedSpecTests.KeyEnv && target == EnvironmentVariableTarget.User ? Key : null;

    private static StubSystemOne JevStub(Func<string, HttpResponseMessage?>? fail = null) => new()
    {
        IsTau = false,
        RoundTo = 2,
        ReturnedModel = "jev-1.13.0",
        Usage = (360, 55),
        Probabilities = CliTests.ModelStub().Probabilities,
        Fail = fail ?? (_ => null),
    };

    private static Func<PipelineOptions, PipelineOptions> With(
        StubSystemOne local, StubSystemOne hosted, Func<string, EnvironmentVariableTarget, string?>? env = null) => o => o with
    {
        EndpointFactory = uri => new WorkbenchEndpoint(uri, local, new RetryPolicy { MaxAttempts = 1 }),
        HostedEndpointFactory = (ext, key) => new WorkbenchEndpoint(ext.Endpoint, hosted, FastHosted, key),
        ReadEnvironment = env ?? UserScopeOnly,
        Measure = MeasureTests.NoGpu,
        Report = ReportTests.FixedContext with { Command = o.Report.Command },
    };

    private static async Task<(int Exit, string Out)> Tau(string[] args, Func<PipelineOptions, PipelineOptions> configure)
    {
        var o = new StringWriter();
        int exit = await TauCli.RunAsync(args, o, new StringWriter(), configure);
        return (exit, o.ToString());
    }

    /// <summary>Runs everything twice (answering the frontier batches in between) and returns the second run's output.</summary>
    private static async Task<(int Exit, string Out)> RunAll(TestRepo repo, StubSystemOne local, StubSystemOne hosted)
    {
        await Tau(["run", repo.SpecPath], With(local, hosted));
        CliTests.AnswerEverything(repo);
        return await Tau(["run", repo.SpecPath], With(local, hosted));
    }

    [Fact]
    public async Task TheHostedEndpointGetsTheKeyAndTheKeyNeverReachesAnOutput()
    {
        using var repo = HostedSpecTests.HostedRepo();
        var local = CliTests.ModelStub();
        var jev = JevStub();
        var (exit, output) = await RunAll(repo, local, jev);

        Assert.Equal(0, exit);
        Assert.Equal(360, jev.Authorizations.Count);
        Assert.All(jev.Authorizations, a => Assert.Equal("Bearer " + Key, a));
        Assert.All(local.Authorizations, Assert.Null);
        Assert.All(jev.PrecisionRequested, Assert.True); // still sent, and ignored
        Assert.All(jev.Calls, c => Assert.True(c.Raw));
        Assert.Contains("measure: jev-test raw: skipped (current)", output, StringComparison.Ordinal);

        // No artefact, summary, calibrator, report or console line holds the key.
        Assert.DoesNotContain(Key, output, StringComparison.Ordinal);
        var files = Directory.EnumerateFiles(repo.Root, "*", SearchOption.AllDirectories).ToArray();
        Assert.Contains(files, f => f.EndsWith("report.json", StringComparison.Ordinal));
        foreach (var f in files)
        {
            Assert.DoesNotContain(Key, File.ReadAllText(f), StringComparison.Ordinal);
        }

        var summary = WorkbenchJson.ReadJson<MeasureSummary>(repo.Spec.RunSummaryPath("jev-test", "heldout", Phases.Raw));
        Assert.Equal(Precisions.Rounded, summary.Precision);
        Assert.Equal(2, summary.DecimalPlaces);
        Assert.Null(summary.ModelHash);
        Assert.Null(summary.Gpu);
        var h = summary.Hosted!;
        Assert.Equal(["jev-1.13.0"], h.ModelsReturned);
        Assert.Equal("jev-latest", h.RequestedModel);
        Assert.Equal("https://hosted.test/", h.Endpoint);
        Assert.Equal(120 * 360, h.InputTokens);
        Assert.Equal(120 * 55, h.OutputTokens);
        Assert.Equal(120 * 360 * 0.042 / 1e6, h.EstimatedSpendUsd, 12);
        Assert.Equal(240 * 360 * 0.042 / 1e6 + h.EstimatedSpendUsd, h.RunSpendUsd, 12);
        Assert.Contains("did not honour them", h.HeadersNote, StringComparison.Ordinal);
        Assert.Contains("rounded to 2 dp", h.HeadersNote, StringComparison.Ordinal);
        Assert.Contains("network included", h.LatencyNote, StringComparison.Ordinal);
        Assert.False(h.StoppedAtBudget);
    }

    [Fact]
    public async Task AMissingKeyBlocksTheHostedEndpointOnlyAndSendsNothing()
    {
        using var repo = HostedSpecTests.HostedRepo();
        var jev = JevStub();
        var (exit, output) = await Tau(["measure", repo.SpecPath], With(CliTests.ModelStub(), jev, env: (_, _) => null));
        Assert.Equal(ExitCodes.Blocked, exit);
        Assert.Contains($"jev-test: blocked: no API key. Set the {HostedSpecTests.KeyEnv} environment variable", output, StringComparison.Ordinal);
        Assert.Empty(jev.Calls);
        Assert.True(File.Exists(repo.Spec.RunSummaryPath("model-a", "heldout", Phases.Raw)));
    }

    [Fact]
    public async Task TheSpendGuardStopsTheMeasureAtTheBudgetAndSavesWhatItHas()
    {
        // Each call costs 360 × $0.042 / 1M = $0.00001512, so a $0.0001 budget allows a handful.
        using var repo = TestRepo.Create(calibration: 240, heldOut: 120, altSubset: 10, targetError: 0.25);
        File.AppendAllText(repo.SpecPath, HostedSpecTests.External(budget: 0.0001, concurrency: 1));
        var jev = JevStub();
        var (exit, output) = await Tau(["measure", repo.SpecPath], With(CliTests.ModelStub(), jev));

        Assert.Equal(ExitCodes.Blocked, exit);
        Assert.Contains("jev-test: blocked: the spend guard stopped the measure at an estimated $0.0001", output, StringComparison.Ordinal);
        var summary = WorkbenchJson.ReadJson<MeasureSummary>(repo.Spec.RunSummaryPath("jev-test", "calibration", Phases.Raw));
        var h = summary.Hosted!;
        Assert.True(h.StoppedAtBudget);
        Assert.Equal(jev.Calls.Count, h.PricedResponses);
        Assert.Equal(240 - jev.Calls.Count, h.NotSent);
        Assert.InRange(h.RunSpendUsd, 0, 0.0001);
        Assert.Equal(5, jev.Calls.Count); // spent 0..4 calls: 4 × 1.512e-5 + 2 × 1.512e-5 = 9.07e-5; at 5 it would be 1.06e-4
        Assert.Equal(240 - 5, summary.FailuresByStatus[MeasureError.NotSentStatus.ToString(CultureInfo.InvariantCulture)]);
        Assert.Contains("The spend guard stopped the run", h.SpendNote, StringComparison.Ordinal);
        Assert.False(File.Exists(repo.Spec.RunSummaryPath("jev-test", "heldout", Phases.Raw))); // never started

        // A run cut short is never treated as current.
        var rerun = await Tau(["run", repo.SpecPath], With(CliTests.ModelStub(), JevStub()));
        Assert.DoesNotContain("jev-test raw: skipped (current)", rerun.Out, StringComparison.Ordinal);
        Assert.Equal(ExitCodes.Blocked, rerun.Exit);
    }

    [Fact]
    public async Task HostedCallsRetryA429AndA5xx()
    {
        using var repo = TestRepo.Create(heldOut: 6);
        var attempts = new ConcurrentDictionary<string, int>();
        var jev = JevStub(text => attempts.AddOrUpdate(text, 1, (_, n) => n + 1) switch
        {
            1 => new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("slow down") },
            2 => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("busy") },
            _ => null,
        });
        var ext = new ExternalModelSpec("jev-test", new Uri("https://hosted.test"), "jev-latest", HostedSpecTests.KeyEnv, 0.042, 0, 1, 2);
        using var endpoint = new WorkbenchEndpoint(ext.Endpoint, jev, FastHosted, Key);
        var identity = new EndpointIdentity(false, "hosted", [], []);
        var items = PreparedDataset.LoadSplit(repo.Spec, repo.Manifest, "heldout");
        var options = MeasureTests.NoGpu with { Hosted = new HostedRun(ext, new SpendGuard(ext)) };
        var summary = await MeasureStage.RunAsync(repo.Spec, "model-a", "heldout", Phases.Raw, items, endpoint, identity, options, TestContext.Current.CancellationToken);

        Assert.Equal(6, summary.Measured);
        Assert.Equal(0, summary.Failures);
        Assert.Equal(18, jev.Calls.Count);
        Assert.Equal(6, summary.Hosted!.PricedResponses);
    }

    [Fact]
    public async Task FailureTextEchoingTheKeyIsRedacted()
    {
        using var repo = TestRepo.Create(heldOut: 4);
        var jev = JevStub(text => text.EndsWith(" 2", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent($"bad credentials: Authorization: Bearer {Key}") }
            : null);
        using var endpoint = new WorkbenchEndpoint(new Uri("https://hosted.test"), jev, FastHosted, Key);
        var items = PreparedDataset.LoadSplit(repo.Spec, repo.Manifest, "heldout");
        var summary = await MeasureStage.RunAsync(repo.Spec, "model-a", "heldout", Phases.Raw, items, endpoint,
            new EndpointIdentity(false, "hosted", [], []), MeasureTests.NoGpu, TestContext.Current.CancellationToken);

        Assert.Equal(1, summary.Failures);
        var text = File.ReadAllText(repo.Spec.RunPath("model-a", "heldout", Phases.Raw));
        Assert.DoesNotContain(Key, text, StringComparison.Ordinal);
        Assert.Contains("Bearer " + WorkbenchEndpoint.Redacted, text, StringComparison.Ordinal);
        Assert.True(endpoint.HasApiKey);
    }

    [Fact]
    public void OfflineOnlyCalibratorsAreNeverLoadedByTheRuntimesLoader()
    {
        using var repo = TestRepo.Create();
        File.AppendAllText(repo.SpecPath, HostedSpecTests.External());
        var spec = repo.Spec;
        Synthetic.WriteRawRuns(repo, "model-a", 2.0, seed: 1);
        Synthetic.WriteRawRuns(repo, "jev-test", 2.0, seed: 2, modelHash: null, tau: false);
        CalibrateStage.Run(spec, "model-a", repo.Manifest, ReportTests.FixedContext.Clock);
        var hosted = CalibrateStage.Run(spec, "jev-test", repo.Manifest, ReportTests.FixedContext.Clock);

        Assert.True(hosted.OfflineOnly);
        Assert.Contains(hosted.Notes, n => n.StartsWith("Offline only:", StringComparison.Ordinal));
        Assert.Equal(WorkbenchJson.Sha256File(spec.RunPath("jev-test", "calibration", Phases.Raw)), hosted.ModelHash);
        var dir = spec.CalibratorsDirectory("jev-test");
        Assert.Empty(Directory.EnumerateFiles(dir, "*.calibrator.json"));
        var offline = Directory.EnumerateFiles(dir, "*" + OfflineCalibrators.Suffix).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(2, offline.Length); // question type and option-count bucket
        Assert.All(offline, f => Assert.Equal("jev-test", OfflineCalibrators.Read(f).Model));
        Assert.All(hosted.Calibrators, c => Assert.EndsWith(OfflineCalibrators.Suffix, c.File, StringComparison.Ordinal));
        Assert.NotNull(hosted.OfflineHeldOut);
        Assert.True(File.Exists(spec.RunPath("jev-test", "heldout", Phases.Offline)));

        // Pointed at the root the Runtime uses, the loader sees the local model's two files and nothing else.
        var set = CalibratorSet.LoadDirectory(spec.CalibratorsRoot);
        Assert.Equal(2, set.All.Count);
        Assert.All(set.All, c => Assert.Equal("model-a", c.Model));
        Assert.Null(set.Find("jev-test", QuestionType.Choice, 4));

        // Even renamed to look like a calibrator, the wrapper fails validation instead of loading with a made-up hash.
        var renamed = Path.Combine(repo.Root, "renamed");
        Directory.CreateDirectory(renamed);
        File.Copy(offline[0], Path.Combine(renamed, "jev-test.choice.calibrator.json"));
        Assert.Throws<CalibratorValidationException>(() => CalibratorSet.LoadDirectory(renamed));
    }

    [Fact]
    public async Task TheReportLabelsTheHostedEndpointAndPricesItsLocalShareAtItsOwnPrice()
    {
        using var repo = HostedSpecTests.HostedRepo();
        var (exit, _) = await RunAll(repo, CliTests.ModelStub(), JevStub());
        Assert.Equal(0, exit);
        var report = JsonNode.Parse(File.ReadAllText(repo.Spec.ReportJsonPath))!;

        var models = report["models"]!.AsArray();
        Assert.Equal(["model-a", "jev-test"], models.Select(m => (string)m!["model"]!));
        Assert.Null(models[0]!["hosted"]);
        var hosted = models[1]!["hosted"]!;
        Assert.Equal(ExternalModelSpec.Label, (string)hosted["label"]!);
        Assert.Equal("jev-1.13.0", (string)hosted["models_returned"]![0]!);
        Assert.Equal(2, (int)hosted["decimal_places"]!);
        Assert.Equal("offline", (string)models[1]!["best_calibrated_source"]!);
        Assert.True((bool)models[1]!["calibration"]!["offline_only"]!);
        Assert.Contains("rounded to 2 dp", report.ToJsonString(), StringComparison.Ordinal);

        var thresholds = report["thresholds"]!.AsArray().Select(t => (string)t!["model"]!).ToArray();
        Assert.Equal(["model-a", "jev-test"], thresholds);
        var cascades = report["cascades"]!.AsArray();
        Assert.Null(cascades[0]!["hosted"]);
        Assert.Null(cascades[0]!["cost"]?["hosted_local"]);
        var jevCascade = cascades.Single(c => (string)c!["model"]! == "jev-test")!;
        Assert.Equal(ExternalModelSpec.Label, (string)jevCascade["hosted"]!);
        var cost = jevCascade["cost"]!;
        Assert.Null(cost["local_kwh_per_decision"]);
        Assert.Equal(360 * 0.042 / 1e6, (double)cost["hosted_local"]!["usd_per_decision"]!, 12);
        Assert.Equal(360 * 0.042 / 1e6 * 0.75, (double)cost["local_gbp_per_decision"]!, 12);
        Assert.Contains(cost["basis"]!.AsArray(), b => ((string)b!).StartsWith("The first-line model here is the hosted endpoint jev-test, not a local GPU", StringComparison.Ordinal));
        Assert.Equal("jev-test", (string)report["metadata"]!["hosted"]![0]!["id"]!);

        var html = File.ReadAllText(repo.Spec.ReportHtmlPath);
        Assert.Contains($"jev-test ({ExternalModelSpec.Label})", html, StringComparison.Ordinal);
        Assert.Contains("(wall clock, network included)", html, StringComparison.Ordinal);
        Assert.Contains("Calibrated view: the Workbench&#39;s offline calibration", html.Replace("&#x27;", "&#39;", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("answered as jev-1.13.0", html, StringComparison.Ordinal);
        Assert.DoesNotContain("model-a (hosted", html, StringComparison.Ordinal);
    }
}
