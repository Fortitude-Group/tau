namespace Tau.Client.Tests;

/// <summary>Tests for how <see cref="SystemOneClientExtensions.DecideAsync{TEnum}(ISystemOneClient,System.Text.Json.Nodes.JsonNode?,string,IReadOnlyDictionary{TEnum,string}?,string,CancellationToken)"/> maps the response.</summary>
public class DecideAsyncTests
{
    [Fact]
    public async Task Maps_probabilities_onto_every_enum_member_and_picks_the_chosen_value()
    {
        var handler = StubHttpMessageHandler.Always(
            Responses.Choice("decision", "High", """{"Low":0.05,"Medium":0.15,"High":0.8}""", 0.72));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        using var client = new SystemOneClient(http);

        var decision = await client.DecideAsync<Urgency>(state: "hello", instructions: "how urgent?", ct: TestContext.Current.CancellationToken);

        Assert.Equal(Urgency.High, decision.Value);
        Assert.Equal(0.05, decision.Probabilities[Urgency.Low]);
        Assert.Equal(0.15, decision.Probabilities[Urgency.Medium]);
        Assert.Equal(0.8, decision.Probabilities[Urgency.High]);
        Assert.Equal(0.72, decision.Confidence);
        Assert.Equal("High", decision.Raw.Choice); // the raw ChoiceAnswer is preserved on the result
    }

    [Fact]
    public async Task A_missing_enum_member_probability_defaults_to_zero()
    {
        var handler = StubHttpMessageHandler.Always(
            Responses.Choice("decision", "Low", """{"Low":1}""", 0.99));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        using var client = new SystemOneClient(http);

        var decision = await client.DecideAsync<Urgency>(state: "hello", instructions: "how urgent?", ct: TestContext.Current.CancellationToken);

        Assert.Equal(1, decision.Probabilities[Urgency.Low]);
        Assert.Equal(0.0, decision.Probabilities[Urgency.Medium]);
        Assert.Equal(0.0, decision.Probabilities[Urgency.High]);
    }

    [Fact]
    public async Task A_chosen_key_matching_no_enum_member_is_a_protocol_error()
    {
        var handler = StubHttpMessageHandler.Always(
            Responses.Choice("decision", "Unknown", """{"Low":0.3,"Medium":0.3,"High":0.4}""", 0.4));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        using var client = new SystemOneClient(http);

        var ex = await Assert.ThrowsAsync<SystemOneProtocolException>(
            () => client.DecideAsync<Urgency>(state: "hello", instructions: "how urgent?", ct: TestContext.Current.CancellationToken));
        Assert.Contains("Unknown", ex.Message);
    }

    [Fact]
    public async Task A_response_missing_the_asked_question_is_a_protocol_error()
    {
        var handler = StubHttpMessageHandler.Always(Responses.Noul("some-other-question", 0.5));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        using var client = new SystemOneClient(http);

        await Assert.ThrowsAsync<SystemOneProtocolException>(
            () => client.DecideAsync<Urgency>(state: "hello", instructions: "how urgent?", ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task String_state_overload_wraps_the_text_as_the_JSON_state()
    {
        var handler = StubHttpMessageHandler.Always(
            Responses.Choice("decision", "Low", """{"Low":1,"Medium":0,"High":0}""", 0.9));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        using var client = new SystemOneClient(http);

        await client.DecideAsync<Urgency>(state: "plain text state", instructions: "how urgent?", ct: TestContext.Current.CancellationToken);

        var body = handler.RequestBodies.Single()!;
        Assert.Contains("\"state\":\"plain text state\"", body);
    }
}
