using System.Text.Json;
using Json.Schema;
using Tau.Contract.Validation;

namespace Tau.Contract.Tests.Validation;

/// <summary>
/// Checks that <see cref="RequestValidator"/> and <c>request.schema.json</c> (evaluated with
/// JsonSchema.Net) reach the same accept/reject verdict on a shared set of samples.
/// </summary>
/// <remarks>
/// One rule is deliberately excluded from this set: the 50-question cap (data-model.md,
/// R-10). <c>request.schema.json</c> constrains <c>questions</c> with only
/// <c>minProperties: 1</c> — it has no <c>maxProperties</c>, because JSON Schema cannot
/// express "reject over 50 entries" any tighter than that without also being satisfied by
/// completely different, unrelated request shapes. That cap is exercised only by
/// <c>RequestValidatorTests.Questions_FiftyOneEntries_IsInvalid</c>, directly against
/// <see cref="RequestValidator"/>, not here.
/// </remarks>
public class SchemaConsistencyTests
{
    private static readonly JsonSchema Schema = JsonSchema.FromText(ContractSchemas.RequestSchemaText);

    public static TheoryData<string, string> Samples()
    {
        var data = new TheoryData<string, string>();

        void Add(string name, string json) => data.Add(name, json);

        // Valid samples.
        Add("valid: minimal choice", """{"model":"m","state":null,"questions":{"q":{"type":"choice","instructions":"x","criteria":{"a":"A"}}}}""");
        Add("valid: minimal score", """{"model":"m","state":null,"questions":{"q":{"type":"score","instructions":"x","criteria":["lo","hi"]}}}""");
        Add("valid: minimal noul, no criteria", """{"model":"m","state":null,"questions":{"q":{"type":"noul","instructions":"x"}}}""");
        Add("valid: noul with true/false criteria", """{"model":"m","state":null,"questions":{"q":{"type":"noul","instructions":"x","criteria":{"true":"y","false":"n"}}}}""");
        Add("valid: noul criteria Title case", """{"model":"m","state":null,"questions":{"q":{"type":"noul","instructions":"x","criteria":{"True":"y","False":"n"}}}}""");
        Add("valid: noul criteria UPPER case", """{"model":"m","state":null,"questions":{"q":{"type":"noul","instructions":"x","criteria":{"TRUE":"y","FALSE":"n"}}}}""");
        Add("valid: structured state object", """{"model":"m","state":{"a":1,"b":[1,2]},"questions":{"q":{"type":"noul","instructions":"x"}}}""");
        Add("valid: structured state array", """{"model":"m","state":[1,2,3],"questions":{"q":{"type":"noul","instructions":"x"}}}""");
        Add("valid: empty string state", """{"model":"m","state":"","questions":{"q":{"type":"noul","instructions":"x"}}}""");
        Add("valid: instructions as object", """{"model":"m","state":null,"questions":{"q":{"type":"noul","instructions":{"a":"b"}}}}""");
        Add("valid: instructions as array", """{"model":"m","state":null,"questions":{"q":{"type":"noul","instructions":["a","b"]}}}""");
        Add("valid: choice criteria value null", """{"model":"m","state":null,"questions":{"q":{"type":"choice","instructions":"x","criteria":{"a":null}}}}""");
        Add("valid: choice criteria value object", """{"model":"m","state":null,"questions":{"q":{"type":"choice","instructions":"x","criteria":{"a":{"d":"x"}}}}}""");
        Add("valid: choice criteria value array", """{"model":"m","state":null,"questions":{"q":{"type":"choice","instructions":"x","criteria":{"a":["x"]}}}}""");
        var tenLevels = "[" + string.Join(",", Enumerable.Range(0, 10).Select(i => $"\"l{i}\"")) + "]";
        Add("valid: score criteria 10 levels", """{"model":"m","state":null,"questions":{"q":{"type":"score","instructions":"x","criteria":""" + tenLevels + """}}}""");

        var twoFiftyFiveOptions = "{" + string.Join(",", Enumerable.Range(0, 255).Select(i => $"\"o{i}\":\"O\"")) + "}";
        Add("valid: choice criteria 255 options", """{"model":"m","state":null,"questions":{"q":{"type":"choice","instructions":"x","criteria":""" + twoFiftyFiveOptions + """}}}""");
        Add("valid: x-tau- extension field", """{"model":"m","state":null,"questions":{"q":{"type":"noul","instructions":"x"}},"x-tau-raw":true}""");
        Add("valid: multiple questions of mixed types", """{"model":"m","state":"s","questions":{"a":{"type":"choice","instructions":"x","criteria":{"a":"A"}},"b":{"type":"score","instructions":"x","criteria":["lo","hi"]},"c":{"type":"noul","instructions":"x"}}}""");

        // Invalid samples.
        Add("invalid: missing model", """{"state":null,"questions":{"q":{"type":"noul","instructions":"x"}}}""");
        Add("invalid: empty model", """{"model":"","state":null,"questions":{"q":{"type":"noul","instructions":"x"}}}""");
        Add("invalid: missing state", """{"model":"m","questions":{"q":{"type":"noul","instructions":"x"}}}""");
        Add("invalid: missing questions", """{"model":"m","state":null}""");
        Add("invalid: empty questions object", """{"model":"m","state":null,"questions":{}}""");
        Add("invalid: questions is an array", """{"model":"m","state":null,"questions":[]}""");
        Add("invalid: question missing type", """{"model":"m","state":null,"questions":{"q":{"instructions":"x"}}}""");
        Add("invalid: question type not a known kind", """{"model":"m","state":null,"questions":{"q":{"type":"banana","instructions":"x"}}}""");
        Add("invalid: question missing instructions", """{"model":"m","state":null,"questions":{"q":{"type":"noul"}}}""");
        Add("invalid: instructions is a number", """{"model":"m","state":null,"questions":{"q":{"type":"noul","instructions":42}}}""");
        Add("invalid: question has unknown field", """{"model":"m","state":null,"questions":{"q":{"type":"noul","instructions":"x","bogus":1}}}""");
        Add("invalid: choice missing criteria", """{"model":"m","state":null,"questions":{"q":{"type":"choice","instructions":"x"}}}""");
        Add("invalid: choice criteria empty", """{"model":"m","state":null,"questions":{"q":{"type":"choice","instructions":"x","criteria":{}}}}""");
        Add("invalid: choice criteria value is a number", """{"model":"m","state":null,"questions":{"q":{"type":"choice","instructions":"x","criteria":{"a":1}}}}""");
        var twoFiftySixOptions = "{" + string.Join(",", Enumerable.Range(0, 256).Select(i => $"\"o{i}\":\"O\"")) + "}";
        Add("invalid: choice criteria 256 options", """{"model":"m","state":null,"questions":{"q":{"type":"choice","instructions":"x","criteria":""" + twoFiftySixOptions + """}}}""");
        Add("invalid: score missing criteria", """{"model":"m","state":null,"questions":{"q":{"type":"score","instructions":"x"}}}""");
        Add("invalid: score criteria is an object", """{"model":"m","state":null,"questions":{"q":{"type":"score","instructions":"x","criteria":{}}}}""");
        Add("invalid: score criteria one level", """{"model":"m","state":null,"questions":{"q":{"type":"score","instructions":"x","criteria":["lo"]}}}""");
        var elevenLevels = "[" + string.Join(",", Enumerable.Range(0, 11).Select(i => $"\"l{i}\"")) + "]";
        Add("invalid: score criteria eleven levels", """{"model":"m","state":null,"questions":{"q":{"type":"score","instructions":"x","criteria":""" + elevenLevels + """}}}""");
        Add("invalid: score criteria item is null", """{"model":"m","state":null,"questions":{"q":{"type":"score","instructions":"x","criteria":[null,"hi"]}}}""");
        Add("invalid: noul criteria unknown key", """{"model":"m","state":null,"questions":{"q":{"type":"noul","instructions":"x","criteria":{"maybe":"y"}}}}""");
        Add("invalid: unknown top-level field", """{"model":"m","state":null,"questions":{"q":{"type":"noul","instructions":"x"}},"bogus":1}""");

        return data;
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void RequestValidatorAndSchemaAgree(string name, string json)
    {
        var validatorAccepts = RequestValidator.Validate(json).Count == 0;

        using var document = JsonDocument.Parse(json);
        var schemaAccepts = Schema.Evaluate(document.RootElement).IsValid;

        Assert.True(
            validatorAccepts == schemaAccepts,
            $"{name}: RequestValidator {(validatorAccepts ? "accepted" : "rejected")}, schema {(schemaAccepts ? "accepted" : "rejected")}");
    }

    [Fact]
    public void AtLeastThirtySamplesAreExercised()
    {
        Assert.True(Samples().Count >= 30, $"expected at least 30 samples, got {Samples().Count}");
    }
}
