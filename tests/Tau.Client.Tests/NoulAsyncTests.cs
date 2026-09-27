namespace Tau.Client.Tests;

/// <summary>Tests for <see cref="SystemOneClientExtensions.NoulAsync(ISystemOneClient,System.Text.Json.Nodes.JsonNode?,string,string?,string?,string,CancellationToken)"/> and its detailed counterpart.</summary>
public class NoulAsyncTests
{
    [Fact]
    public async Task Happy_path_returns_the_probability_of_true()
    {
        var handler = StubHttpMessageHandler.Always(Responses.Noul("decision", 0.83));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        using var client = new SystemOneClient(http);

        var probability = await client.NoulAsync(state: "hello", instructions: "is this urgent?", ct: TestContext.Current.CancellationToken);

        Assert.Equal(0.83, probability);
    }

    [Fact]
    public async Task Detailed_overload_also_returns_the_raw_answer()
    {
        var handler = StubHttpMessageHandler.Always(Responses.Noul("decision", 0.27));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        using var client = new SystemOneClient(http);

        var result = await client.NoulDetailedAsync(state: "hello", instructions: "is this urgent?", ct: TestContext.Current.CancellationToken);

        Assert.Equal(0.27, result.Value);
        Assert.Equal(0.27, result.Raw.Noul);
        Assert.Equal("noul", result.Raw.Type);
    }

    [Fact]
    public async Task A_response_without_a_noul_answer_is_a_protocol_error()
    {
        var handler = StubHttpMessageHandler.Always(
            Responses.Choice("decision", "Low", """{"Low":1,"Medium":0,"High":0}""", 0.9));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        using var client = new SystemOneClient(http);

        await Assert.ThrowsAsync<SystemOneProtocolException>(
            () => client.NoulAsync(state: "hello", instructions: "is this urgent?", ct: TestContext.Current.CancellationToken));
    }
}
