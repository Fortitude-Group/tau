using System.Net;
using System.Text.Json.Nodes;
using Tau.Contract;

namespace Tau.Client.Tests;

/// <summary>Tests for <see cref="SystemOneClient"/>'s exponential-backoff retry of 429/529 responses.</summary>
public class RetryTests
{
    private static readonly RetryPolicy FastRetryPolicy = new()
    {
        BaseDelay = TimeSpan.FromMilliseconds(1),
        DelayAsync = static (_, _) => Task.CompletedTask,
    };

    private static DecisionRequest SampleRequest() => new()
    {
        Model = "jev-latest",
        State = JsonValue.Create("hello"),
        Questions = new OrderedDictionary<string, Question>
        {
            ["decision"] = new NoulQuestion { Instructions = JsonValue.Create("is this urgent?")! },
        },
    };

    [Fact]
    public async Task Retries_a_429_then_succeeds()
    {
        var handler = StubHttpMessageHandler.Sequence(
            Responses.Text(HttpStatusCode.TooManyRequests, "slow down"),
            Responses.Noul("decision", 0.5));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        using var client = new SystemOneClient(http, FastRetryPolicy);

        var response = await client.SystemOneAsync(SampleRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("jev-1.13.0", response.Model);
    }

    [Fact]
    public async Task Retries_a_529_then_succeeds()
    {
        var handler = StubHttpMessageHandler.Sequence(
            Responses.Text((HttpStatusCode)529, "overloaded"),
            Responses.Noul("decision", 0.5));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        using var client = new SystemOneClient(http, FastRetryPolicy);

        await client.SystemOneAsync(SampleRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Never_retries_a_400()
    {
        var handler = StubHttpMessageHandler.Always(Responses.Text(HttpStatusCode.BadRequest, "bad request"));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        using var client = new SystemOneClient(http, FastRetryPolicy);

        await Assert.ThrowsAsync<SystemOneHttpException>(
            () => client.SystemOneAsync(SampleRequest(), TestContext.Current.CancellationToken));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Gives_up_after_MaxAttempts_and_reports_the_last_status()
    {
        // A factory, not a fixed instance: each attempt reads and disposes its own response, so a
        // fixed HttpResponseMessage would be disposed after the first attempt and blow up on retry.
        var handler = StubHttpMessageHandler.Always(() => Responses.Text(HttpStatusCode.TooManyRequests, "still slow"));
        var policy = FastRetryPolicy with { MaxAttempts = 3 };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        using var client = new SystemOneClient(http, policy);

        var ex = await Assert.ThrowsAsync<SystemOneHttpException>(
            () => client.SystemOneAsync(SampleRequest(), TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.TooManyRequests, ex.StatusCode);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task MaxAttempts_of_one_means_no_retries_even_for_a_429()
    {
        var handler = StubHttpMessageHandler.Always(Responses.Text(HttpStatusCode.TooManyRequests, "slow down"));
        var policy = FastRetryPolicy with { MaxAttempts = 1 };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        using var client = new SystemOneClient(http, policy);

        await Assert.ThrowsAsync<SystemOneHttpException>(
            () => client.SystemOneAsync(SampleRequest(), TestContext.Current.CancellationToken));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Backoff_delay_doubles_on_each_retry()
    {
        var observedDelays = new List<TimeSpan>();
        var policy = new RetryPolicy
        {
            BaseDelay = TimeSpan.FromMilliseconds(10),
            MaxAttempts = 3,
            DelayAsync = (delay, _) =>
            {
                observedDelays.Add(delay);
                return Task.CompletedTask;
            },
        };
        var handler = StubHttpMessageHandler.Always(() => Responses.Text(HttpStatusCode.TooManyRequests, "slow down"));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        using var client = new SystemOneClient(http, policy);

        await Assert.ThrowsAsync<SystemOneHttpException>(
            () => client.SystemOneAsync(SampleRequest(), TestContext.Current.CancellationToken));

        Assert.Equal([TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20)], observedDelays);
    }
}
