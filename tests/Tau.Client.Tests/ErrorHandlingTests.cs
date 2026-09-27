using System.Net;
using System.Text.Json.Nodes;
using Tau.Contract;

namespace Tau.Client.Tests;

/// <summary>Tests for how <see cref="SystemOneClient"/> turns non-success or unusable responses into exceptions.</summary>
public class ErrorHandlingTests
{
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
    public async Task A_422_with_Taus_shape_is_parsed_into_ValidationProblems()
    {
        var handler = StubHttpMessageHandler.Always(
            Responses.ValidationError(("questions.decision.criteria", "score needs 2-10 levels, got 1")));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        using var client = new SystemOneClient(http);

        var ex = await Assert.ThrowsAsync<SystemOneValidationException>(
            () => client.SystemOneAsync(SampleRequest(), TestContext.Current.CancellationToken));

        var problem = Assert.Single(ex.Problems);
        Assert.Equal("questions.decision.criteria", problem.Path);
        Assert.Equal("score needs 2-10 levels, got 1", problem.Problem);
        Assert.Contains("questions.decision.criteria", ex.RawBody);
    }

    [Fact]
    public async Task A_422_in_a_non_Tau_shape_keeps_the_raw_body_under_a_single_dollar_problem()
    {
        const string jevStyleBody = """{"error":"invalid_request","message":"model not found"}""";
        var handler = StubHttpMessageHandler.Always(Responses.Json(HttpStatusCode.UnprocessableEntity, jevStyleBody));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        using var client = new SystemOneClient(http);

        var ex = await Assert.ThrowsAsync<SystemOneValidationException>(
            () => client.SystemOneAsync(SampleRequest(), TestContext.Current.CancellationToken));

        var problem = Assert.Single(ex.Problems);
        Assert.Equal("$", problem.Path);
        Assert.Equal(jevStyleBody, ex.RawBody);
    }

    [Fact]
    public async Task A_401_becomes_a_SystemOneHttpException_with_the_body_preserved()
    {
        var handler = StubHttpMessageHandler.Always(Responses.Text(HttpStatusCode.Unauthorized, "missing API key"));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        using var client = new SystemOneClient(http);

        var ex = await Assert.ThrowsAsync<SystemOneHttpException>(
            () => client.SystemOneAsync(SampleRequest(), TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        Assert.Equal("missing API key", ex.Body);
    }

    [Fact]
    public async Task A_500_becomes_a_SystemOneHttpException()
    {
        var handler = StubHttpMessageHandler.Always(Responses.Text(HttpStatusCode.InternalServerError, "boom"));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        using var client = new SystemOneClient(http);

        var ex = await Assert.ThrowsAsync<SystemOneHttpException>(
            () => client.SystemOneAsync(SampleRequest(), TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
    }

    [Fact]
    public async Task A_malformed_200_body_is_a_protocol_error()
    {
        var handler = StubHttpMessageHandler.Always(Responses.Text(HttpStatusCode.OK, "not json at all"));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        using var client = new SystemOneClient(http);

        await Assert.ThrowsAsync<SystemOneProtocolException>(
            () => client.SystemOneAsync(SampleRequest(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_200_body_missing_required_response_fields_is_a_protocol_error()
    {
        var handler = StubHttpMessageHandler.Always(Responses.Json(HttpStatusCode.OK, """{"model":"jev-1.13.0"}"""));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        using var client = new SystemOneClient(http);

        var ex = await Assert.ThrowsAsync<SystemOneProtocolException>(
            () => client.SystemOneAsync(SampleRequest(), TestContext.Current.CancellationToken));
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public async Task Cancellation_is_honoured()
    {
        var handler = StubHttpMessageHandler.Always(Responses.Noul("decision", 0.5));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        using var client = new SystemOneClient(http);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SystemOneAsync(SampleRequest(), cts.Token));
    }
}
