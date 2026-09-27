using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Json.Schema;
using Tau.Contract;
using Tau.Inference.Engine;

namespace Tau.Runtime.Tests.Http;

/// <summary>The HTTP surface, with a fake engine: contract shape, 422s, headers, discovery, health and /metrics.</summary>
public sealed class HostTests(FakeEngineFactory factory) : IClassFixture<FakeEngineFactory>
{
    private static JsonSchema ResponseSchema => TestSchemas.Response;

    private const string Valid = """
        {"model":"jev-latest","state":"I was charged twice.","questions":{
          "queue":{"type":"choice","instructions":"Which team?","criteria":{"billing":"money","tech":"bugs"}},
          "urgency":{"type":"score","instructions":"How urgent?","criteria":["low","high"]},
          "churn":{"type":"noul","instructions":"Leaving?"}}}
        """;

    private async Task<HttpResponseMessage> PostAsync(string body, Action<HttpRequestMessage>? tweak = null)
    {
        var client = factory.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Post, "/v1/systemone")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        tweak?.Invoke(req);
        return await client.SendAsync(req, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Valid_request_returns_a_strictly_contract_shaped_response()
    {
        var res = await PostAsync(Valid);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var text = await res.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = System.Text.Json.JsonDocument.Parse(text);
        Assert.True(ResponseSchema.Evaluate(doc.RootElement).IsValid, text);
        var json = JsonNode.Parse(text);
        Assert.Equal(["queue", "urgency", "churn"], json!["answers"]!.AsObject().Select(kv => kv.Key));
    }

    [Fact]
    public async Task Diagnostics_go_in_x_tau_headers_not_the_body()
    {
        var res = await PostAsync(Valid);
        Assert.Equal(new string('a', 64), res.Headers.GetValues("x-tau-model-hash").Single());
        Assert.Equal("none", res.Headers.GetValues("x-tau-calibrators").Single());
        Assert.Equal("false", res.Headers.GetValues("x-tau-truncated").Single());
        Assert.Equal("English Latin text (?)", res.Headers.GetValues("x-tau-route-reason").Single());
        Assert.Equal("1.5", res.Headers.GetValues("x-tau-model-ms").Single());
    }

    [Fact]
    public async Task Authorization_header_is_accepted_and_ignored()
    {
        var res = await PostAsync(Valid, r => r.Headers.Add("Authorization", "Bearer sk-not-a-real-key"));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Raw_flag_reaches_the_engine()
    {
        await PostAsync(Valid, r => r.Headers.Add("x-tau-raw", "true"));
        Assert.True(factory.Engine.Calls[^1].Options.Raw);
        await PostAsync(Valid);
        Assert.False(factory.Engine.Calls[^1].Options.Raw);
    }

    [Theory]
    [InlineData("{not json", "$")]
    [InlineData("""{"state":"x","questions":{"q":{"type":"noul","instructions":"y"}}}""", "model")]
    [InlineData("""{"model":"auto","questions":{"q":{"type":"noul","instructions":"y"}}}""", "state")]
    [InlineData("""{"model":"auto","state":"x","questions":{}}""", "questions")]
    [InlineData("""{"model":"auto","state":"x","questions":{"q":{"type":"maybe","instructions":"y"}}}""", "questions.q")]
    [InlineData("""{"model":"auto","state":"x","questions":{"q":{"type":"score","instructions":"y","criteria":["one"]}}}""", "questions.q.criteria")]
    [InlineData("""{"model":"auto","state":"x","extra":1,"questions":{"q":{"type":"noul","instructions":"y"}}}""", "extra")]
    public async Task Contract_violations_are_422_naming_the_field(string body, string pathFragment)
    {
        var res = await PostAsync(body);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        var json = JsonNode.Parse(await res.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        Assert.Equal("validation_error", json["error"]!["type"]!.GetValue<string>());
        Assert.Contains(json["error"]!["details"]!.AsArray(), d => d!["path"]!.GetValue<string>().Contains(pathFragment));
    }

    [Fact]
    public async Task Engine_rejections_are_422_with_the_same_body_shape()
    {
        factory.Engine.Reject = r => r.Model == "no-such-model"
            ? new DecisionRejectedException("model", "unknown model 'no-such-model'; installed: laya-en")
            : null;
        try
        {
            var res = await PostAsync(Valid.Replace("jev-latest", "no-such-model"));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
            var json = JsonNode.Parse(await res.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
            Assert.Equal("model", json["error"]!["details"]![0]!["path"]!.GetValue<string>());
        }
        finally { factory.Engine.Reject = _ => null; }
    }

    [Fact]
    public async Task Oversized_body_is_rejected()
    {
        var huge = Valid.Replace("I was charged twice.", new string('x', 2 << 20));
        var res = await PostAsync(huge);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, res.StatusCode);
    }

    [Fact]
    public async Task Models_and_health_are_discoverable()
    {
        var client = factory.CreateClient();
        var models = JsonNode.Parse(await client.GetStringAsync("/v1/models", TestContext.Current.CancellationToken))!;
        Assert.Equal("laya-en", models["models"]![0]!["id"]!.GetValue<string>());
        Assert.Equal(new string('a', 64), models["models"]![0]!["onnx_sha256"]!.GetValue<string>());
        Assert.Contains("jev-latest", models["aliases"]!.AsArray().Select(a => a!.GetValue<string>()));
        var health = await client.GetAsync("/healthz", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task Metrics_expose_the_request_series_after_a_request()
    {
        await PostAsync(Valid);
        var client = factory.CreateClient();
        // The Prometheus exporter caches scrapes briefly; poll for the series rather than assume timing.
        string text = "";
        for (var i = 0; i < 20 && !text.Contains("tau_batch_rows"); i++)
        {
            text = await client.GetStringAsync("/metrics", TestContext.Current.CancellationToken);
            if (!text.Contains("tau_batch_rows")) await Task.Delay(250, TestContext.Current.CancellationToken);
        }
        Assert.Contains("tau_request_duration", text);
        Assert.Contains("tau_model_duration", text);
        Assert.Contains("tau_input_tokens", text);
        Assert.Contains("tau_batch_rows", text);
        Assert.Contains("model=\"laya-en\"", text);
    }
}
