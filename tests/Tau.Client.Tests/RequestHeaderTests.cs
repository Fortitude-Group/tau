using System.Net;
using System.Text.Json.Nodes;
using Tau.Contract;

namespace Tau.Client.Tests;

/// <summary>
/// Per-request headers on <see cref="SystemOneClient.SystemOneAsync(DecisionRequest, IReadOnlyDictionary{string, string}?, CancellationToken)"/>,
/// used by the Workbench to ask for raw reference probabilities with <c>x-tau-raw: true</c>.
/// </summary>
public class RequestHeaderTests
{
    private static DecisionRequest SampleRequest() => new()
    {
        Model = "laya-en",
        State = JsonValue.Create("hello"),
        Questions = new OrderedDictionary<string, Question>
        {
            ["decision"] = new NoulQuestion { Instructions = JsonValue.Create("is this urgent?")! },
        },
    };

    private static readonly Dictionary<string, string> Raw = new() { ["x-tau-raw"] = "true" };

    private static (StubHttpMessageHandler Handler, SystemOneClient Client, HttpClient Http) Create(StubHttpMessageHandler? handler = null)
    {
        handler ??= StubHttpMessageHandler.Always(() => Responses.Noul("decision", 0.5));
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        return (handler, new SystemOneClient(http), http);
    }

    [Fact]
    public async Task X_tau_raw_is_sent_when_given()
    {
        var (handler, client, http) = Create();
        using var _ = http;

        await client.SystemOneAsync(SampleRequest(), Raw, TestContext.Current.CancellationToken);

        Assert.Equal(["true"], handler.Requests.Single().Headers.GetValues("x-tau-raw"));
    }

    [Fact]
    public async Task No_x_tau_raw_is_sent_by_the_original_overload()
    {
        var (handler, client, http) = Create();
        using var _ = http;

        await client.SystemOneAsync(SampleRequest(), TestContext.Current.CancellationToken);

        Assert.False(handler.Requests.Single().Headers.Contains("x-tau-raw"));
    }

    [Fact]
    public async Task Null_or_empty_headers_send_nothing_extra()
    {
        var (handler, client, http) = Create();
        using var _ = http;

        await client.SystemOneAsync(SampleRequest(), null, TestContext.Current.CancellationToken);
        await client.SystemOneAsync(SampleRequest(), new Dictionary<string, string>(), TestContext.Current.CancellationToken);

        Assert.All(handler.Requests, r => Assert.False(r.Headers.Contains("x-tau-raw")));
    }

    [Fact]
    public async Task Headers_apply_to_that_request_only()
    {
        var (handler, client, http) = Create();
        using var _ = http;

        await client.SystemOneAsync(SampleRequest(), Raw, TestContext.Current.CancellationToken);
        await client.SystemOneAsync(SampleRequest(), TestContext.Current.CancellationToken);

        Assert.True(handler.Requests[0].Headers.Contains("x-tau-raw"));
        Assert.False(handler.Requests[1].Headers.Contains("x-tau-raw"));
        Assert.False(http.DefaultRequestHeaders.Contains("x-tau-raw"));
    }

    [Fact]
    public async Task Headers_are_resent_on_every_retry()
    {
        var handler = StubHttpMessageHandler.Sequence(
            Responses.Text(HttpStatusCode.TooManyRequests, "slow down"),
            Responses.Noul("decision", 0.5));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        using var client = new SystemOneClient(http, new RetryPolicy
        {
            BaseDelay = TimeSpan.FromMilliseconds(1),
            DelayAsync = static (_, _) => Task.CompletedTask,
        });

        await client.SystemOneAsync(SampleRequest(), Raw, TestContext.Current.CancellationToken);

        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, r => Assert.Equal(["true"], r.Headers.GetValues("x-tau-raw")));
    }

    [Fact]
    public async Task Headers_work_through_the_owned_HttpClient_constructor_alongside_the_api_key()
    {
        var handler = StubHttpMessageHandler.Always(() => Responses.Noul("decision", 0.5));
        using var client = new SystemOneClient(new Uri("http://localhost/"), handler, apiKey: "sk-test-123");

        await client.SystemOneAsync(SampleRequest(), Raw, TestContext.Current.CancellationToken);

        var request = handler.Requests.Single();
        Assert.Equal(["true"], request.Headers.GetValues("x-tau-raw"));
        Assert.Equal("sk-test-123", request.Headers.Authorization!.Parameter);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("bad header")]
    [InlineData("Content-Type")]
    public async Task Invalid_header_names_are_rejected_before_anything_is_sent(string name)
    {
        var (handler, client, http) = Create();
        using var _ = http;

        await Assert.ThrowsAsync<ArgumentException>(() => client.SystemOneAsync(
            SampleRequest(), new Dictionary<string, string> { [name] = "x" }, TestContext.Current.CancellationToken));

        Assert.Empty(handler.Requests);
    }
}
