using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tau.Contract.Tests;

/// <summary>
/// Round-trips the contract's documented example shapes through <see cref="ContractParser"/>,
/// and checks that question, criteria and extension-field order survive parsing and
/// re-serialisation exactly.
/// </summary>
public class RoundTripTests
{
    private const string ExampleRequest = """
    {
        "model": "laya-en",
        "state": {"customer": "not happy", "notes": ["late", "rude"]},
        "questions": {
            "urgency": {
                "type": "score",
                "instructions": "How urgent is this?",
                "criteria": ["low", "medium", "high"]
            },
            "category": {
                "type": "choice",
                "instructions": "Pick a category",
                "criteria": {"billing": "Billing issue", "support": "Support issue", "other": null}
            },
            "escalate": {
                "type": "noul",
                "instructions": "Should this be escalated?",
                "criteria": {"True": "yes, escalate", "FALSE": "no"}
            }
        },
        "x-tau-debug": true
    }
    """;

    [Fact]
    public void ParsesTheDocumentedExampleWithNoProblems()
    {
        var ok = ContractParser.TryParse(ExampleRequest, out var request, out var problems);

        Assert.True(ok);
        Assert.Empty(problems);
        Assert.NotNull(request);
    }

    [Fact]
    public void PreservesModelAndState()
    {
        ContractParser.TryParse(ExampleRequest, out var request, out _);

        Assert.Equal("laya-en", request!.Model);
        Assert.IsType<JsonObject>(request.State);
        Assert.Equal("not happy", request.State!["customer"]!.GetValue<string>());
    }

    [Fact]
    public void PreservesQuestionOrderExactly()
    {
        ContractParser.TryParse(ExampleRequest, out var request, out _);

        Assert.Equal(["urgency", "category", "escalate"], request!.Questions.Keys);
    }

    [Fact]
    public void ScoreQuestionPreservesLevelOrder()
    {
        ContractParser.TryParse(ExampleRequest, out var request, out _);

        var score = Assert.IsType<ScoreQuestion>(request!.Questions["urgency"]);
        Assert.Equal(3, score.Criteria.Count);
        Assert.Equal(["low", "medium", "high"], score.Criteria.Select(node => node.GetValue<string>()));
    }

    [Fact]
    public void ChoiceQuestionPreservesOptionOrderAndAllowsNullDescription()
    {
        ContractParser.TryParse(ExampleRequest, out var request, out _);

        var choice = Assert.IsType<ChoiceQuestion>(request!.Questions["category"]);
        Assert.Equal(["billing", "support", "other"], choice.Criteria.Select(kvp => kvp.Key));
        Assert.Null(choice.Criteria[2].Value);
    }

    [Fact]
    public void NoulQuestionNormalisesCriteriaKeysToLowerCase()
    {
        ContractParser.TryParse(ExampleRequest, out var request, out _);

        var noul = Assert.IsType<NoulQuestion>(request!.Questions["escalate"]);
        Assert.NotNull(noul.Criteria);
        Assert.Equal("yes, escalate", noul.Criteria!["true"]!.GetValue<string>());
        Assert.Equal("no", noul.Criteria!["false"]!.GetValue<string>());
    }

    [Fact]
    public void CapturesXTauExtensionFields()
    {
        ContractParser.TryParse(ExampleRequest, out var request, out _);

        Assert.NotNull(request!.Extensions);
        Assert.True(request.Extensions!["x-tau-debug"]!.GetValue<bool>());
    }

    [Fact]
    public void SerialisingAndReparsingPreservesQuestionOrder()
    {
        ContractParser.TryParse(ExampleRequest, out var request, out _);

        var reserialised = JsonSerializer.Serialize(request, ContractJson.Options);
        var node = JsonNode.Parse(reserialised)!.AsObject();
        var questionKeys = node["questions"]!.AsObject().Select(kvp => kvp.Key);

        Assert.Equal(["urgency", "category", "escalate"], questionKeys);
    }

    [Fact]
    public void SerialisingAndReparsingRoundTripsToAnEquivalentRequest()
    {
        ContractParser.TryParse(ExampleRequest, out var request, out _);

        var reserialised = JsonSerializer.Serialize(request, ContractJson.Options);
        var ok = ContractParser.TryParse(reserialised, out var reparsed, out var problems);

        Assert.True(ok);
        Assert.Empty(problems);
        Assert.Equal(request!.Model, reparsed!.Model);
        Assert.Equal(request.Questions.Keys, reparsed.Questions.Keys);
    }
}
