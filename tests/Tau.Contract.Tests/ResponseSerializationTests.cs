using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace Tau.Contract.Tests;

/// <summary>
/// Serialises example <see cref="DecisionResponse"/> values and checks the result against
/// <c>response.schema.json</c> with JsonSchema.Net, confirming it contains exactly the
/// contract's fields and nothing else (the schema is strict: <c>additionalProperties: false</c>
/// throughout).
/// </summary>
public class ResponseSerializationTests
{
    private static readonly JsonSchema Schema = JsonSchema.FromText(ContractSchemas.ResponseSchemaText);

    private static DecisionResponse ExampleResponse() => new()
    {
        Model = "laya-en",
        Answers = new OrderedDictionary<string, Answer>
        {
            ["urgency"] = new ScoreAnswer
            {
                Score = 1.4,
                Legend = new OrderedDictionary<string, string> { ["0"] = "low", ["1"] = "medium", ["2"] = "high" },
                Probabilities = new OrderedDictionary<string, double> { ["0"] = 0.1, ["1"] = 0.5, ["2"] = 0.4 },
                Confidence = 0.72,
            },
            ["category"] = new ChoiceAnswer
            {
                Choice = "billing",
                Probabilities = new OrderedDictionary<string, double> { ["billing"] = 0.7, ["support"] = 0.2, ["other"] = 0.1 },
                Confidence = 0.7,
            },
            ["escalate"] = new NoulAnswer { Noul = 0.83 },
        },
        Usage = new Usage { InputTokens = 128, OutputTokens = 0 },
    };

    [Fact]
    public void SerialisedResponse_ValidatesAgainstTheResponseSchema()
    {
        var json = JsonSerializer.Serialize(ExampleResponse(), ContractJson.Options);
        using var instance = JsonDocument.Parse(json);

        var result = Schema.Evaluate(instance.RootElement);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ChoiceAnswer_SerialisesExactlyTheContractFields()
    {
        var json = JsonSerializer.Serialize(ExampleResponse(), ContractJson.Options);
        var category = JsonNode.Parse(json)!["answers"]!["category"]!.AsObject();

        Assert.Equal(["type", "choice", "probabilities", "confidence"], category.Select(kvp => kvp.Key));
    }

    [Fact]
    public void ScoreAnswer_SerialisesExactlyTheContractFields()
    {
        var json = JsonSerializer.Serialize(ExampleResponse(), ContractJson.Options);
        var urgency = JsonNode.Parse(json)!["answers"]!["urgency"]!.AsObject();

        Assert.Equal(["type", "score", "legend", "probabilities", "confidence"], urgency.Select(kvp => kvp.Key));
    }

    [Fact]
    public void NoulAnswer_SerialisesExactlyTheContractFields()
    {
        var json = JsonSerializer.Serialize(ExampleResponse(), ContractJson.Options);
        var escalate = JsonNode.Parse(json)!["answers"]!["escalate"]!.AsObject();

        Assert.Equal(["type", "noul"], escalate.Select(kvp => kvp.Key));
    }

    [Fact]
    public void Usage_UsesSnakeCaseWireNames()
    {
        var json = JsonSerializer.Serialize(ExampleResponse(), ContractJson.Options);
        var usage = JsonNode.Parse(json)!["usage"]!.AsObject();

        Assert.Equal(["input_tokens", "output_tokens"], usage.Select(kvp => kvp.Key));
        Assert.Equal(128, usage["input_tokens"]!.GetValue<int>());
        Assert.Equal(0, usage["output_tokens"]!.GetValue<int>());
    }

    [Fact]
    public void TopLevelResponse_HasExactlyTheContractFields()
    {
        var json = JsonSerializer.Serialize(ExampleResponse(), ContractJson.Options);
        var root = JsonNode.Parse(json)!.AsObject();

        Assert.Equal(["model", "answers", "usage"], root.Select(kvp => kvp.Key));
    }
}
