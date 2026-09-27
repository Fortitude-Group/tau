using System.Text.Json.Nodes;

namespace Tau.Runtime.Tests.Models;

/// <summary>
/// SC-005 / FR-013 / FR-016: identical requests give identical bytes, sequentially or 100 at once; a question's
/// answer doesn't depend on the other questions in its request.
/// </summary>
[Collection(RealEngineCollection.Name)]
[Trait("Category", "Models")]
public sealed class DeterminismTests(RealEngineFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();
    private const string Ticket = "My card was charged twice and support hasn't replied in three days.";

    [Theory]
    [InlineData("laya-en")]
    [InlineData("von-1.2.0")]
    public async Task Same_request_same_bytes_sequentially_and_concurrently(string model)
    {
        var body = Http.Request(model, Ticket, Http.Three);
        var first = (await Http.PostAsync(_client, body)).Body;
        Assert.Equal(first, (await Http.PostAsync(_client, body)).Body);

        var concurrent = await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => Http.PostAsync(_client, body)));
        Assert.All(concurrent, r =>
        {
            Assert.Equal(200, r.Status);
            Assert.Equal(first, r.Body);
        });
    }

    [Theory]
    [InlineData("laya-en")]
    [InlineData("laya-multilingual")]
    [InlineData("von-1.2.0")]
    public async Task A_question_alone_answers_as_it_does_among_others(string model)
    {
        var together = JsonNode.Parse((await Http.PostAsync(_client, Http.Request(model, Ticket, Http.Three))).Body)!["answers"]!;
        foreach (var (key, q) in Http.Three)
        {
            var single = new JsonObject { [key] = q!.DeepClone() };
            var alone = JsonNode.Parse((await Http.PostAsync(_client, Http.Request(model, Ticket, single))).Body)!["answers"]![key]!;
            AssertSameAnswer($"{model}/{key}", together[key]!, alone);
        }
    }

    /// <summary>
    /// Same choice and the same values. Batch shape (padding to the longest row) can move a float32 result by about
    /// 1e-7, which very occasionally flips the last rounded digit; that is recorded, not hidden: the tolerance here is
    /// one unit of the reference's rounding (1e-4), and the observed maximum is printed.
    /// </summary>
    private static void AssertSameAnswer(string where, JsonNode a, JsonNode b)
    {
        if (a["choice"] is { } ca) Assert.Equal(ca.GetValue<string>(), b["choice"]!.GetValue<string>());
        double max = 0;
        foreach (var field in new[] { "noul", "score", "confidence" })
            if (a[field] is { } va) max = Math.Max(max, Math.Abs(va.GetValue<double>() - b[field]!.GetValue<double>()));
        if (a["probabilities"] is JsonObject pa)
            foreach (var (k, v) in pa) max = Math.Max(max, Math.Abs(v!.GetValue<double>() - b["probabilities"]![k]!.GetValue<double>()));
        TestContext.Current.SendDiagnosticMessage($"{where}: max |Δ| alone vs together = {max:E1}");
        Assert.True(max <= 1e-4 + 1e-12, $"{where}: answer changed by {max} when asked alone");
    }
}
