namespace Tau.Client.Tests;

/// <summary>Tests for <see cref="SystemOneClientExtensions.ScoreAsync(ISystemOneClient,System.Text.Json.Nodes.JsonNode?,string,IReadOnlyList{string},string,CancellationToken)"/>.</summary>
public class ScoreAsyncTests
{
    [Fact]
    public async Task Happy_path_returns_score_probabilities_confidence_and_legend()
    {
        var handler = StubHttpMessageHandler.Always(
            Responses.Score("decision", 1.7, """{"0":"low","1":"medium","2":"high"}""", """{"0":0.1,"1":0.2,"2":0.7}""", 0.65));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        using var client = new SystemOneClient(http);

        var result = await client.ScoreAsync(
            state: "hello", instructions: "how severe?", levels: ["low", "medium", "high"], ct: TestContext.Current.CancellationToken);

        Assert.Equal(1.7, result.Score);
        Assert.Equal(0.65, result.Confidence);
        Assert.Equal([0.1, 0.2, 0.7], result.Probabilities);
        Assert.Equal(["low", "medium", "high"], result.Legend);
        Assert.Equal(0.7, result.Raw.Probabilities["2"]);
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(0.49, 0)]
    [InlineData(0.5, 1)]
    [InlineData(1.49, 1)]
    [InlineData(1.5, 2)]
    [InlineData(2.0, 2)]
    public async Task Level_is_the_score_rounded_away_from_zero_and_clamped_to_the_level_range(double score, int expectedLevel)
    {
        var handler = StubHttpMessageHandler.Always(
            Responses.Score("decision", score, """{"0":"low","1":"medium","2":"high"}""", """{"0":0.34,"1":0.33,"2":0.33}""", 0.4));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        using var client = new SystemOneClient(http);

        var result = await client.ScoreAsync(
            state: "hello", instructions: "how severe?", levels: ["low", "medium", "high"], ct: TestContext.Current.CancellationToken);

        Assert.Equal(expectedLevel, result.Level);
    }

    [Fact]
    public async Task Fewer_than_two_levels_is_rejected_before_any_request_is_sent()
    {
        var handler = StubHttpMessageHandler.Always(Responses.Noul("decision", 0.5));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        using var client = new SystemOneClient(http);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.ScoreAsync(state: "hello", instructions: "how severe?", levels: ["only-one"], ct: TestContext.Current.CancellationToken));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task More_than_ten_levels_is_rejected_before_any_request_is_sent()
    {
        var handler = StubHttpMessageHandler.Always(Responses.Noul("decision", 0.5));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        using var client = new SystemOneClient(http);

        var levels = Enumerable.Range(0, 11).Select(i => $"level-{i}").ToList();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.ScoreAsync(state: "hello", instructions: "how severe?", levels: levels, ct: TestContext.Current.CancellationToken));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_response_without_a_score_answer_is_a_protocol_error()
    {
        var handler = StubHttpMessageHandler.Always(Responses.Noul("decision", 0.5));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        using var client = new SystemOneClient(http);

        await Assert.ThrowsAsync<SystemOneProtocolException>(
            () => client.ScoreAsync(state: "hello", instructions: "how severe?", levels: ["low", "high"], ct: TestContext.Current.CancellationToken));
    }
}
