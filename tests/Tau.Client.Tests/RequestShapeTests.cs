using System.Text.Json;

namespace Tau.Client.Tests;

/// <summary>Tests for the exact JSON and headers <see cref="SystemOneClient"/> puts on the wire.</summary>
public class RequestShapeTests
{
    [Fact]
    public async Task DecideAsync_sends_enum_member_names_as_choice_criteria_in_declaration_order()
    {
        var handler = StubHttpMessageHandler.Always(Responses.Choice("decision", "Low", """{"Low":1,"Medium":0,"High":0}""", 0.9));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        using var client = new SystemOneClient(http);

        await client.DecideAsync<Urgency>(state: "hello", instructions: "how urgent?", ct: TestContext.Current.CancellationToken);

        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://localhost/v1/systemone", request.RequestUri!.ToString());

        var doc = JsonDocument.Parse(handler.RequestBodies.Single()!);
        var root = doc.RootElement;
        Assert.Equal("jev-latest", root.GetProperty("model").GetString());
        Assert.Equal("hello", root.GetProperty("state").GetString());

        var question = root.GetProperty("questions").GetProperty("decision");
        Assert.Equal("choice", question.GetProperty("type").GetString());
        var keys = question.GetProperty("criteria").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(["Low", "Medium", "High"], keys);
    }

    [Fact]
    public async Task DecideAsync_omits_descriptions_that_were_not_supplied()
    {
        var handler = StubHttpMessageHandler.Always(Responses.Choice("decision", "Low", """{"Low":1,"Medium":0,"High":0}""", 0.9));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        using var client = new SystemOneClient(http);

        await client.DecideAsync<Urgency>(
            state: (System.Text.Json.Nodes.JsonNode?)null,
            instructions: "how urgent?",
            descriptions: new Dictionary<Urgency, string> { [Urgency.High] = "needs same-day attention" },
            ct: TestContext.Current.CancellationToken);

        var doc = JsonDocument.Parse(handler.RequestBodies.Single()!);
        var criteria = doc.RootElement.GetProperty("questions").GetProperty("decision").GetProperty("criteria");
        Assert.Equal(JsonValueKind.Null, criteria.GetProperty("Low").ValueKind);
        Assert.Equal("needs same-day attention", criteria.GetProperty("High").GetString());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("state").ValueKind);
    }

    [Fact]
    public async Task DecideAsync_uses_JsonStringEnumMemberName_as_the_wire_name_when_present()
    {
        var handler = StubHttpMessageHandler.Always(Responses.Choice("decision", "in-progress", """{"in-progress":1,"done":0}""", 0.9));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        using var client = new SystemOneClient(http);

        await client.DecideAsync<JsonNamedStatus>(state: "hello", instructions: "status?", ct: TestContext.Current.CancellationToken);

        var criteria = JsonDocument.Parse(handler.RequestBodies.Single()!)
            .RootElement.GetProperty("questions").GetProperty("decision").GetProperty("criteria");
        Assert.True(criteria.TryGetProperty("in-progress", out _));
        Assert.True(criteria.TryGetProperty("done", out _));
    }

    [Fact]
    public async Task DecideAsync_uses_EnumMember_as_the_wire_name_when_present()
    {
        var handler = StubHttpMessageHandler.Always(Responses.Choice("decision", "legacy-open", """{"legacy-open":1,"legacy-closed":0}""", 0.9));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        using var client = new SystemOneClient(http);

        await client.DecideAsync<LegacyNamedStatus>(state: "hello", instructions: "status?", ct: TestContext.Current.CancellationToken);

        var criteria = JsonDocument.Parse(handler.RequestBodies.Single()!)
            .RootElement.GetProperty("questions").GetProperty("decision").GetProperty("criteria");
        Assert.True(criteria.TryGetProperty("legacy-open", out _));
        Assert.True(criteria.TryGetProperty("legacy-closed", out _));
    }

    [Fact]
    public async Task ScoreAsync_sends_levels_as_an_ordered_criteria_array()
    {
        var handler = StubHttpMessageHandler.Always(
            Responses.Score("decision", 1.0, """{"0":"low","1":"medium","2":"high"}""", """{"0":0.1,"1":0.2,"2":0.7}""", 0.8));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        using var client = new SystemOneClient(http);

        await client.ScoreAsync(state: "hello", instructions: "how bad?", levels: ["low", "medium", "high"], ct: TestContext.Current.CancellationToken);

        var question = JsonDocument.Parse(handler.RequestBodies.Single()!)
            .RootElement.GetProperty("questions").GetProperty("decision");
        Assert.Equal("score", question.GetProperty("type").GetString());
        var criteria = question.GetProperty("criteria").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(["low", "medium", "high"], criteria);
    }

    [Fact]
    public async Task NoulAsync_sends_null_criteria_when_no_descriptions_are_given()
    {
        var handler = StubHttpMessageHandler.Always(Responses.Noul("decision", 0.5));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        using var client = new SystemOneClient(http);

        await client.NoulAsync(state: "hello", instructions: "is this urgent?", ct: TestContext.Current.CancellationToken);

        var question = JsonDocument.Parse(handler.RequestBodies.Single()!)
            .RootElement.GetProperty("questions").GetProperty("decision");
        Assert.Equal("noul", question.GetProperty("type").GetString());

        // Tau.Contract's NoulQuestion always writes the "criteria" property; with no
        // descriptions supplied its value is JSON null rather than the key being absent.
        Assert.True(question.TryGetProperty("criteria", out var criteria));
        Assert.Equal(JsonValueKind.Null, criteria.ValueKind);
    }

    [Fact]
    public async Task NoulAsync_sends_true_false_criteria_when_descriptions_are_given()
    {
        var handler = StubHttpMessageHandler.Always(Responses.Noul("decision", 0.5));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        using var client = new SystemOneClient(http);

        await client.NoulAsync(
            state: "hello",
            instructions: "is this urgent?",
            trueDescription: "yes, urgent",
            falseDescription: "no rush",
            ct: TestContext.Current.CancellationToken);

        var criteria = JsonDocument.Parse(handler.RequestBodies.Single()!)
            .RootElement.GetProperty("questions").GetProperty("decision").GetProperty("criteria");
        Assert.Equal("yes, urgent", criteria.GetProperty("true").GetString());
        Assert.Equal("no rush", criteria.GetProperty("false").GetString());
    }

    [Fact]
    public async Task Bearer_header_is_sent_when_an_api_key_is_given()
    {
        var handler = StubHttpMessageHandler.Always(Responses.Noul("decision", 0.5));
        using var client = new SystemOneClient(new Uri("http://localhost/"), handler, apiKey: "sk-test-123");

        await client.NoulAsync(state: "hello", instructions: "is this urgent?", ct: TestContext.Current.CancellationToken);

        var authHeader = handler.Requests.Single().Headers.Authorization;
        Assert.NotNull(authHeader);
        Assert.Equal("Bearer", authHeader!.Scheme);
        Assert.Equal("sk-test-123", authHeader.Parameter);
    }

    [Fact]
    public async Task No_bearer_header_is_sent_when_no_api_key_is_given()
    {
        var handler = StubHttpMessageHandler.Always(Responses.Noul("decision", 0.5));
        using var client = new SystemOneClient(new Uri("http://localhost/"), handler);

        await client.NoulAsync(state: "hello", instructions: "is this urgent?", ct: TestContext.Current.CancellationToken);

        Assert.Null(handler.Requests.Single().Headers.Authorization);
    }

    [Fact]
    public async Task Base_url_without_a_trailing_slash_is_normalised_so_the_path_still_combines()
    {
        var handler = StubHttpMessageHandler.Always(Responses.Noul("decision", 0.5));
        using var client = new SystemOneClient(new Uri("http://localhost:5000"), handler);

        await client.NoulAsync(state: "hello", instructions: "is this urgent?", ct: TestContext.Current.CancellationToken);

        Assert.Equal("http://localhost:5000/v1/systemone", handler.Requests.Single().RequestUri!.ToString());
    }
}
