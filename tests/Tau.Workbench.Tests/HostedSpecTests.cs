using System.Globalization;
using Tau.Calibration;
using Tau.Contract;
using Tau.Workbench.Measure;
using Tau.Workbench.Spec;

namespace Tau.Workbench.Tests;

/// <summary>The <c>external:</c> spec block, API-key reading, the spend guard and precision detection. No network.</summary>
public sealed class HostedSpecTests
{
    internal const string KeyEnv = "TAU_TEST_HOSTED_KEY";

    /// <summary>An <c>external:</c> block for one hosted endpoint, with any line replaced or added.</summary>
    internal static string External(double budget = 1.0, int concurrency = 4, double inputPrice = 0.042, params string[] overrides)
    {
        var lines = new List<(string Key, string Value)>
        {
            ("id", "jev-test"),
            ("endpoint", "https://hosted.test"),
            ("model", "jev-latest"),
            ("api_key_env", KeyEnv),
            ("price_usd_per_mtok", $"{{ input: {inputPrice.ToString(CultureInfo.InvariantCulture)}, output: 0 }}"),
            ("budget_usd", budget.ToString(CultureInfo.InvariantCulture)),
            ("concurrency", concurrency.ToString(CultureInfo.InvariantCulture)),
        };
        foreach (var o in overrides)
        {
            var key = o[..o.IndexOf(':', StringComparison.Ordinal)];
            var value = o[(o.IndexOf(':', StringComparison.Ordinal) + 1)..].Trim();
            int i = lines.FindIndex(l => l.Key == key);
            if (i >= 0)
            {
                lines[i] = (key, value);
            }
            else
            {
                lines.Add((key, value));
            }
        }

        return "\nexternal:\n" + string.Concat(lines.Select((l, i) => $"{(i == 0 ? "  - " : "    ")}{l.Key}: {l.Value}\n"));
    }

    internal static TestRepo HostedRepo(double budget = 1.0, int concurrency = 4, int calibration = 240, int heldOut = 120)
    {
        var repo = TestRepo.Create(calibration: calibration, heldOut: heldOut, altSubset: 10, targetError: 0.25);
        File.AppendAllText(repo.SpecPath, External(budget, concurrency));
        return repo;
    }

    [Fact]
    public void TheExternalBlockParsesAndJoinsTheModelList()
    {
        using var repo = HostedRepo();
        var spec = repo.Spec;
        var ext = Assert.Single(spec.External);
        Assert.Equal("jev-test", ext.Id);
        Assert.Equal(new Uri("https://hosted.test"), ext.Endpoint);
        Assert.Equal("jev-latest", ext.Model);
        Assert.Equal(KeyEnv, ext.ApiKeyEnv);
        Assert.Equal(0.042, ext.InputUsdPerMTok);
        Assert.Equal(0, ext.OutputUsdPerMTok);
        Assert.Equal(1.0, ext.BudgetUsd);
        Assert.Equal(4, ext.Concurrency);
        Assert.Equal(["model-a"], spec.Models);
        Assert.Equal(["model-a", "jev-test"], spec.AllModels);
        Assert.Same(ext, spec.ExternalFor("jev-test"));
        Assert.Null(spec.ExternalFor("model-a"));
    }

    [Fact]
    public void ASpecWithoutAnExternalBlockHasNone()
    {
        using var repo = TestRepo.Create();
        Assert.Empty(repo.Spec.External);
        Assert.Equal(repo.Spec.Models, repo.Spec.AllModels);
    }

    [Theory]
    [InlineData("id: model-a", "is also a model or baseline name")]
    [InlineData("id: ../escape", "must be a simple name")]
    [InlineData("api_key_env: sk-live-abc123", "not the key itself")]
    [InlineData("budget_usd: 0", "budget_usd must be a positive number")]
    [InlineData("endpoint: https://user:secret@hosted.test", "must not carry credentials")]
    [InlineData("endpoint: https://hosted.test/?key=abc", "must not carry credentials")]
    [InlineData("endpoint: ftp://hosted.test", "must be an absolute http(s) URL")]
    [InlineData("concurrency: 0", "concurrency must be a whole number")]
    [InlineData("price_usd_per_mtok: { input: -1, output: 0 }", "must not be negative")]
    [InlineData("price_usd_per_mtok: { input: 1, output: 0, cached: 0 }", "unknown key 'external[0].price_usd_per_mtok.cached'")]
    [InlineData("api_key: sk-live-abc123", "unknown key 'external[0].api_key'")]
    [InlineData("model: \"\"", "model must name the model")]
    public void BadExternalEntriesAreRejected(string line, string expected)
    {
        using var repo = TestRepo.Create();
        File.AppendAllText(repo.SpecPath, External(overrides: line));
        var e = Assert.Throws<SpecValidationException>(() => repo.Spec);
        Assert.Contains(e.Problems, p => p.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void AMissingPriceOrBudgetIsRejected()
    {
        using var repo = TestRepo.Create();
        File.AppendAllText(repo.SpecPath, "\nexternal:\n  - id: jev-test\n    endpoint: https://hosted.test\n    model: jev-latest\n    api_key_env: X\n");
        var e = Assert.Throws<SpecValidationException>(() => repo.Spec);
        Assert.Contains(e.Problems, p => p.Contains("missing required section 'external[0].price_usd_per_mtok'", StringComparison.Ordinal));
        Assert.Contains(e.Problems, p => p.Contains("missing required key 'external[0].budget_usd'", StringComparison.Ordinal));
    }

    [Fact]
    public void TheKeyIsReadFromTheProcessFirstThenTheUserScope()
    {
        var env = new Dictionary<(string, EnvironmentVariableTarget), string>
        {
            [("BOTH", EnvironmentVariableTarget.Process)] = "from-process",
            [("BOTH", EnvironmentVariableTarget.User)] = "from-user",
            [("USER_ONLY", EnvironmentVariableTarget.User)] = "  from-user-only  ",
            [("BLANK", EnvironmentVariableTarget.Process)] = "   ",
            [("MACHINE_ONLY", EnvironmentVariableTarget.Machine)] = "from-machine",
        };
        string? Read(string name, EnvironmentVariableTarget target) => env.GetValueOrDefault((name, target));
        Assert.Equal("from-process", ApiKeys.Read("BOTH", Read));
        Assert.Equal("from-user-only", ApiKeys.Read("USER_ONLY", Read));
        Assert.Null(ApiKeys.Read("BLANK", Read));
        Assert.Null(ApiKeys.Read("MACHINE_ONLY", Read));
        Assert.Null(ApiKeys.Read("MISSING", Read));
    }

    [Fact]
    public void TheSpendGuardStopsBeforeTheBudgetWouldBeExceeded()
    {
        // $1 per input token, so every request with 1 input token costs exactly $1.
        var spec = new ExternalModelSpec("jev-test", new Uri("https://hosted.test"), "jev-latest", KeyEnv, 1_000_000, 0, BudgetUsd: 5, Concurrency: 1);
        var guard = new SpendGuard(spec);
        var one = new Usage { InputTokens = 1, OutputTokens = 0 };
        int sent = 0;
        while (guard.TryStart())
        {
            sent++;
            guard.Finish(one);
        }

        // Spent 0, 1, 2, 3: each projection (spent + average × 2) is at most 5. At 4 spent the next projection is 6.
        Assert.Equal(4, sent);
        Assert.Equal(4, guard.SpentUsd);
        Assert.True(guard.Stopped);
        Assert.False(guard.TryStart());
        Assert.Equal(4, guard.InputTokens);
    }

    [Fact]
    public void TheSpendGuardCountsRequestsInFlight()
    {
        var spec = new ExternalModelSpec("jev-test", new Uri("https://hosted.test"), "jev-latest", KeyEnv, 1_000_000, 0, BudgetUsd: 3.5, Concurrency: 4);
        var guard = new SpendGuard(spec);
        var one = new Usage { InputTokens = 1, OutputTokens = 0 };
        Assert.True(guard.TryStart());
        guard.Finish(one); // spent 1, average 1
        Assert.True(guard.TryStart()); // nothing in flight: 1 + 1 × (0 + 2) = 3
        Assert.False(guard.TryStart()); // one in flight: 1 + 1 × (1 + 2) = 4, over 3.5
    }

    [Fact]
    public void AFailedCallIsNotPriced()
    {
        var spec = new ExternalModelSpec("jev-test", new Uri("https://hosted.test"), "jev-latest", KeyEnv, 1_000_000, 0, BudgetUsd: 1, Concurrency: 1);
        var guard = new SpendGuard(spec);
        Assert.True(guard.TryStart());
        guard.Finish(null);
        Assert.Equal(0, guard.SpentUsd);
        Assert.Equal(0, guard.Priced);
        Assert.True(guard.TryStart());
    }

    [Theory]
    [InlineData(0.37, 2)]
    [InlineData(0.5, 1)]
    [InlineData(0.1234, 4)]
    [InlineData(0.0, 0)]
    [InlineData(1.0, 0)]
    [InlineData(1e-5, 5)]
    [InlineData(1.5e-7, 8)]
    [InlineData(0.30000000000000004, 17)]
    public void DecimalPlacesAreReadFromTheNumberAsWritten(double value, int expected) =>
        Assert.Equal(expected, Precisions.DecimalPlaces(value));

    [Fact]
    public void MaxDecimalPlacesReadsOnlyWhatTheEndpointReturned()
    {
        using var repo = TestRepo.Create(type: "noul", heldOut: 2);
        var q = repo.Spec.Question;
        // A noul record stores [1 - p, p]; 1 - 0.37 is 0.63 but 1 - 0.1 is 0.9, and neither was returned.
        var records = new[]
        {
            new MeasuredItem { ItemId = "h0", Gold = "true" }.WithVector(q, [1 - 0.37, 0.37]),
            new MeasuredItem { ItemId = "h1", Gold = "false" }.WithVector(q, [1 - 0.1, 0.1]),
            new MeasuredItem { ItemId = "h2", Gold = "false", Error = new MeasureError(500, "boom") },
        };
        Assert.Equal(2, Precisions.MaxDecimalPlaces(q, records));
        Assert.Null(Precisions.MaxDecimalPlaces(q, [records[2]]));
        Assert.Equal(QuestionType.Noul, q.Type);
    }
}
