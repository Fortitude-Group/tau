using System.Net;
using System.Text.Json.Nodes;

namespace Tau.Runtime.Tests.Models;

/// <summary>US2 with the real models: contract-strict answers for every state shape and every model, routing, rejections, edge cases.</summary>
[Collection(RealEngineCollection.Name)]
[Trait("Category", "Models")]
public sealed class RealContractTests(RealEngineFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();
    private const string Ticket = "I was charged twice for my order and I want a refund today, or I'm cancelling.";

    public static TheoryData<string> Models => new("laya-en", "laya-multilingual", "laya-typed-decisions", "von-1.2.0");

    public static TheoryData<string> States => new(
        "\"" + Ticket + "\"",
        """{"subject":"Double charge","body":"refund please","vip":true,"amount":49.99}""",
        """[{"role":"user","text":"charged twice"},{"role":"agent","text":"sorry, order number?"}]""",
        """{"customer":{"tier":"gold","tags":["billing"]},"messages":[{"text":"refund"}]}""",
        "null",
        "\"\"");

    [Theory]
    [MemberData(nameof(Models))]
    public async Task Every_model_answers_all_three_types_in_contract_shape(string model)
    {
        var (status, body, res) = await Http.PostAsync(_client, Http.Request(model, Ticket, Http.Three));
        Assert.Equal(200, status);
        Http.AssertContractValid(body);
        var json = JsonNode.Parse(body)!;
        Assert.Equal(model, json["model"]!.GetValue<string>());
        Assert.Equal(["queue", "urgency", "churn"], json["answers"]!.AsObject().Select(kv => kv.Key));
        var probs = json["answers"]!["queue"]!["probabilities"]!.AsObject().Select(kv => kv.Value!.GetValue<double>()).Sum();
        Assert.InRange(probs, 1 - 1e-3, 1 + 1e-3);
        Assert.True(json["usage"]!["input_tokens"]!.GetValue<int>() > 0);
        Assert.Equal(0, json["usage"]!["output_tokens"]!.GetValue<int>());
        Assert.Equal(64, res.Headers.GetValues("x-tau-model-hash").Single().Length);
    }

    [Theory]
    [MemberData(nameof(States))]
    public async Task Structured_null_and_empty_states_are_answered(string stateJson)
    {
        var (status, body, _) = await Http.PostAsync(_client, Http.Request("laya-en", JsonNode.Parse(stateJson), Http.Three));
        Assert.Equal(200, status);
        Http.AssertContractValid(body);
    }

    [Fact]
    public async Task Sdk_default_model_routes_by_script()
    {
        var q = new JsonObject { ["refund"] = new JsonObject { ["type"] = "noul", ["instructions"] = "Is the customer asking for money back?" } };
        var (_, en, enRes) = await Http.PostAsync(_client, Http.Request("jev-latest", Ticket, q));
        Assert.Equal("laya-en", JsonNode.Parse(en)!["model"]!.GetValue<string>());
        Assert.Equal("English Latin text", enRes.Headers.GetValues("x-tau-route-reason").Single());

        var (_, de, _) = await Http.PostAsync(_client, Http.Request("jev-latest", "Mir wurde meine Bestellung zweimal berechnet und ich möchte heute eine Rückerstattung.", q));
        Assert.Equal("laya-multilingual", JsonNode.Parse(de)!["model"]!.GetValue<string>());

        var (_, hi, _) = await Http.PostAsync(_client, Http.Request("auto", "मेरे ऑर्डर के लिए मुझसे दो बार शुल्क लिया गया", q));
        Assert.Equal("laya-multilingual", JsonNode.Parse(hi)!["model"]!.GetValue<string>());
    }

    [Fact]
    public async Task Unknown_model_is_422_listing_the_installed_ids()
    {
        var (status, body, _) = await Http.PostAsync(_client, Http.Request("gpt-5", Ticket, Http.Three));
        Assert.Equal(422, status);
        var detail = JsonNode.Parse(body)!["error"]!["details"]![0]!;
        Assert.Equal("model", detail["path"]!.GetValue<string>());
        foreach (var id in new[] { "laya-en", "laya-multilingual", "laya-typed-decisions", "von-1.2.0" })
            Assert.Contains(id, detail["problem"]!.GetValue<string>());
    }

    [Fact]
    public async Task Options_over_the_laya_en_head_budget_are_422_naming_the_question()
    {
        var opts = new JsonObject();
        for (var i = 0; i < 255; i++) opts[$"option_{i:000}"] = $"description of choice number {i}";
        var q = new JsonObject { ["pick"] = new JsonObject { ["type"] = "choice", ["instructions"] = "Pick.", ["criteria"] = opts } };
        var (status, body, _) = await Http.PostAsync(_client, Http.Request("laya-en", Ticket, q));
        Assert.Equal(422, status);
        Assert.Equal("questions.pick.criteria", JsonNode.Parse(body)!["error"]!["details"]![0]!["path"]!.GetValue<string>());

        // The 1024-token checkpoints fit all 255 (as the reference does).
        var (ok, okBody, _) = await Http.PostAsync(_client, Http.Request("laya-multilingual", Ticket, q));
        Assert.Equal(200, ok);
        Http.AssertContractValid(okBody);
    }

    [Fact]
    public async Task Long_state_is_truncated_and_says_so()
    {
        var longState = string.Join(" ", Enumerable.Range(0, 400).Select(i => $"update {i}: still no refund for the double charge."));
        var (status, body, res) = await Http.PostAsync(_client, Http.Request("laya-en", longState, Http.Three));
        Assert.Equal(200, status);
        Http.AssertContractValid(body);
        Assert.Equal("true", res.Headers.GetValues("x-tau-truncated").Single());
    }

    [Fact]
    public async Task Special_tokens_duplicates_and_fifty_questions_are_handled()
    {
        var special = JsonNode.Parse("""{"q":{"type":"choice","instructions":"Pick [MASK].","criteria":{"[MASK] billing":"money [SEP]","tech":"bugs"}}}""")!.AsObject();
        Assert.Equal(200, (await Http.PostAsync(_client, Http.Request("laya-en", "Refund [MASK] me [SEP] now", special))).Status);

        var dup = JsonNode.Parse("""{"q":{"type":"choice","instructions":"Pick.","criteria":{"a":"same text","b":"same text","c":"other"}}}""")!.AsObject();
        var (_, dupBody, _) = await Http.PostAsync(_client, Http.Request("von-1.2.0", Ticket, dup));
        Assert.Equal(["a", "b", "c"], JsonNode.Parse(dupBody)!["answers"]!["q"]!["probabilities"]!.AsObject().Select(kv => kv.Key));

        var fifty = new JsonObject();
        for (var i = 0; i < 50; i++) fifty[$"q{i:00}"] = new JsonObject { ["type"] = "noul", ["instructions"] = $"Is statement {i} true?" };
        var (status, body, res) = await Http.PostAsync(_client, Http.Request("laya-en", Ticket, fifty));
        Assert.Equal(200, status);
        Http.AssertContractValid(body);
        Assert.Equal(50, JsonNode.Parse(body)!["answers"]!.AsObject().Count);
    }

    [Fact]
    public async Task Von_rejects_what_its_reference_rejects()
    {
        var structured = JsonNode.Parse("""{"q":{"type":"choice","instructions":"Pick.","criteria":{"billing":{"desc":"money"},"tech":"bugs"}}}""")!.AsObject();
        var (status, body, _) = await Http.PostAsync(_client, Http.Request("von-1.2.0", Ticket, structured));
        Assert.Equal(422, status);
        Assert.Equal("questions.q.criteria.billing", JsonNode.Parse(body)!["error"]!["details"]![0]!["path"]!.GetValue<string>());
        // Laya renders the same structured criterion as JSON and answers it (its reference does).
        Assert.Equal(200, (await Http.PostAsync(_client, Http.Request("laya-en", Ticket, structured))).Status);
    }

    [Fact]
    public async Task Authorization_header_is_ignored()
    {
        var (status, _, _) = await Http.PostAsync(_client, Http.Request("laya-en", Ticket, Http.Three),
            r => r.Headers.Add("Authorization", "Bearer not-a-real-key"));
        Assert.Equal(200, status);
    }
}
