using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Tau.Client;

namespace Tau.Client.Tests;

/// <summary>
/// T052 (models half): the typed client against the real Runtime, in-process, on CPU with the exported laya-en
/// model. Proves the enum round trip a .NET developer relies on, end to end.
/// </summary>
[Trait("Category", "Models")]
public sealed class RuntimeRoundTripTests : IClassFixture<RuntimeRoundTripTests.Factory>
{
    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Tau:Provider", "cpu");
            builder.UseSetting("Tau:Models:0", "laya-en");
        }
    }

    public enum Team { Billing, Technical, Account }

    private readonly SystemOneClient _client;

    public RuntimeRoundTripTests(Factory factory) => _client = new SystemOneClient(factory.CreateClient());

    [Fact]
    public async Task Decide_returns_an_enum_member_with_a_probability_for_every_member()
    {
        var d = await _client.DecideAsync<Team>(
            "I was charged twice for my order and I want a refund today.",
            "Which team should own this ticket?",
            new Dictionary<Team, string>
            {
                [Team.Billing] = "refunds, double charges, invoices",
                [Team.Technical] = "outages, bugs, error messages",
                [Team.Account] = "logins, passwords, profile changes",
            },
            model: "laya-en",
            ct: TestContext.Current.CancellationToken);

        Assert.Equal(Team.Billing, d.Value);
        Assert.Equal([Team.Billing, Team.Technical, Team.Account], d.Probabilities.Keys);
        Assert.InRange(d.Probabilities.Values.Sum(), 1 - 1e-3, 1 + 1e-3);
        Assert.InRange(d.Confidence, 0, 1);
    }

    [Fact]
    public async Task Score_and_noul_round_trip()
    {
        var s = await _client.ScoreAsync("The site is down for every customer and we're losing sales.",
            "How urgent is this ticket?", ["low", "medium", "high", "critical"], model: "laya-en", ct: TestContext.Current.CancellationToken);
        Assert.InRange(s.Score, 0, 3);
        Assert.Equal(4, s.Probabilities.Count);

        var n = await _client.NoulAsync("Refund me or I'm cancelling.", "Does the customer threaten to leave?",
            model: "laya-en", ct: TestContext.Current.CancellationToken);
        Assert.InRange(n, 0, 1);
    }

    [Fact]
    public async Task Contract_violations_surface_as_a_typed_validation_error()
    {
        var e = await Assert.ThrowsAsync<SystemOneValidationException>(() =>
            _client.ScoreAsync("x", "Rate it.", ["only one level"], model: "laya-en", ct: TestContext.Current.CancellationToken));
        Assert.Contains(e.Problems, p => p.Path.EndsWith("criteria", StringComparison.Ordinal));
    }
}
