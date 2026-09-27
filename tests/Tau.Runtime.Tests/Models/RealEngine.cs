using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Tau.Contract;

namespace Tau.Runtime.Tests.Models;

/// <summary>The real Runtime (CPU, all four exported models) in-process. Model loading is the expensive part, so one instance is shared.</summary>
public sealed class RealEngineFactory : WebApplicationFactory<Program>
{
    /// <summary>Extra <c>Tau:*</c> settings (per-test factories only; the shared fixture uses the defaults).</summary>
    public Dictionary<string, string?> Settings { get; init; } = [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Tau:Provider", "cpu");
        builder.UseSetting("Tau:Preload", "true");
        foreach (var (k, v) in Settings) builder.UseSetting(k, v);
    }
}

[CollectionDefinition(Name)]
public sealed class RealEngineCollection : ICollectionFixture<RealEngineFactory>
{
    public const string Name = "real-engine";
}

internal static class Http
{
    public static JsonSchema ResponseSchema => TestSchemas.Response;

    public static async Task<(int Status, string Body, HttpResponseMessage Response)> PostAsync(HttpClient client, string body,
        Action<HttpRequestMessage>? tweak = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/v1/systemone") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        tweak?.Invoke(req);
        var res = await client.SendAsync(req, TestContext.Current.CancellationToken);
        return ((int)res.StatusCode, await res.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), res);
    }

    public static void AssertContractValid(string body)
    {
        using var doc = JsonDocument.Parse(body);
        Assert.True(ResponseSchema.Evaluate(doc.RootElement).IsValid, "response violates the strict contract schema: " + body);
    }

    public static string Request(string model, JsonNode? state, JsonObject questions) =>
        new JsonObject { ["model"] = model, ["state"] = state?.DeepClone(), ["questions"] = questions.DeepClone() }.ToJsonString();

    public static readonly JsonObject Three = JsonNode.Parse("""
        {"queue":{"type":"choice","instructions":"Which team should own this ticket?","criteria":{"billing":"refunds, double charges","technical":"outages, bugs","account":"logins, passwords"}},
         "urgency":{"type":"score","instructions":"How urgent is this ticket?","criteria":["low","medium","high","critical"]},
         "churn":{"type":"noul","instructions":"Does the customer threaten to leave?"}}
        """)!.AsObject();
}
