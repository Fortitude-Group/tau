using Tau.Contract.Validation;

namespace Tau.Contract.Tests.Validation;

/// <summary>
/// One test per <see cref="RequestValidator"/> rule, with boundaries, per data-model.md and R-10.
/// </summary>
public class RequestValidatorTests
{
    private const string ValidQuestions = """
        "questions": { "q": { "type": "choice", "instructions": "pick", "criteria": { "a": "A" } } }
        """;

    private static string Request(string body) => "{" + body + "}";

    [Fact]
    public void MalformedJson_ReportsSingleProblemAtRoot()
    {
        var problems = RequestValidator.Validate("{not json");

        var problem = Assert.Single(problems);
        Assert.Equal("$", problem.Path);
    }

    [Fact]
    public void RootMustBeAnObject()
    {
        var problems = RequestValidator.Validate("[1,2,3]");

        Assert.Contains(problems, p => p.Path == "$");
    }

    [Fact]
    public void Model_Required()
    {
        var json = Request($"""
            "state": null, {ValidQuestions}
            """);

        var problems = RequestValidator.Validate(json);

        Assert.Contains(problems, p => p.Path == "model");
    }

    [Fact]
    public void Model_MustBeNonEmptyString()
    {
        var json = Request($"""
            "model": "", "state": null, {ValidQuestions}
            """);

        var problems = RequestValidator.Validate(json);

        Assert.Contains(problems, p => p.Path == "model");
    }

    [Fact]
    public void Model_NonEmptyString_IsValid()
    {
        var json = Request($"""
            "model": "laya-en", "state": null, {ValidQuestions}
            """);

        var problems = RequestValidator.Validate(json);

        Assert.Empty(problems);
    }

    [Fact]
    public void State_KeyMustBePresent()
    {
        var json = Request($"""
            "model": "laya-en", {ValidQuestions}
            """);

        var problems = RequestValidator.Validate(json);

        Assert.Contains(problems, p => p.Path == "state");
    }

    [Fact]
    public void State_PresentAndNull_IsValid()
    {
        var json = Request($"""
            "model": "laya-en", "state": null, {ValidQuestions}
            """);

        var problems = RequestValidator.Validate(json);

        Assert.Empty(problems);
    }

    [Fact]
    public void State_EmptyString_IsValid()
    {
        var json = Request($"""
            "model": "laya-en", "state": "", {ValidQuestions}
            """);

        var problems = RequestValidator.Validate(json);

        Assert.Empty(problems);
    }

    [Fact]
    public void State_Structured_IsValid()
    {
        var json = Request(
            "\"model\": \"laya-en\", \"state\": {\"a\": [1,2,{\"b\": null}]}, " + ValidQuestions);

        var problems = RequestValidator.Validate(json);

        Assert.Empty(problems);
    }

    [Fact]
    public void Questions_Required()
    {
        var json = Request("""
            "model": "laya-en", "state": null
            """);

        var problems = RequestValidator.Validate(json);

        Assert.Contains(problems, p => p.Path == "questions");
    }

    [Fact]
    public void Questions_MustBeAnObject()
    {
        var json = Request("""
            "model": "laya-en", "state": null, "questions": []
            """);

        var problems = RequestValidator.Validate(json);

        Assert.Contains(problems, p => p.Path == "questions");
    }

    [Fact]
    public void Questions_ZeroEntries_IsInvalid()
    {
        var json = Request("""
            "model": "laya-en", "state": null, "questions": {}
            """);

        var problems = RequestValidator.Validate(json);

        Assert.Contains(problems, p => p.Path == "questions");
    }

    [Fact]
    public void Questions_OneEntry_IsValid()
    {
        var problems = RequestValidator.Validate(Request($"""
            "model": "laya-en", "state": null, {ValidQuestions}
            """));

        Assert.Empty(problems);
    }

    [Fact]
    public void Questions_FiftyEntries_IsValid()
    {
        var json = Request($"""
            "model": "laya-en", "state": null, "questions": {BuildQuestions(50)}
            """);

        var problems = RequestValidator.Validate(json);

        Assert.Empty(problems);
    }

    [Fact]
    public void Questions_FiftyOneEntries_IsInvalid()
    {
        var json = Request($"""
            "model": "laya-en", "state": null, "questions": {BuildQuestions(51)}
            """);

        var problems = RequestValidator.Validate(json);

        Assert.Contains(problems, p => p.Path == "questions");
    }

    private static string BuildQuestions(int count)
    {
        var entries = Enumerable.Range(0, count)
            .Select(i => $$"""
                "q{{i}}": { "type": "noul", "instructions": "is it?" }
                """);
        return "{" + string.Join(",", entries) + "}";
    }

    [Fact]
    public void Question_MustBeAnObject()
    {
        var json = Request("""
            "model": "laya-en", "state": null, "questions": { "q": "not an object" }
            """);

        var problems = RequestValidator.Validate(json);

        Assert.Contains(problems, p => p.Path == "questions.q");
    }

    [Fact]
    public void Question_Type_Required()
    {
        var json = Request("""
            "model": "laya-en", "state": null,
            "questions": { "q": { "instructions": "x" } }
            """);

        var problems = RequestValidator.Validate(json);

        Assert.Contains(problems, p => p.Path == "questions.q.type");
    }

    [Fact]
    public void Question_Type_MustBeOneOfTheThreeKinds()
    {
        var json = Request("""
            "model": "laya-en", "state": null,
            "questions": { "q": { "type": "banana", "instructions": "x" } }
            """);

        var problems = RequestValidator.Validate(json);

        Assert.Contains(problems, p => p.Path == "questions.q.type");
    }

    [Theory]
    [InlineData("choice")]
    [InlineData("score")]
    [InlineData("noul")]
    public void Question_Type_EachKind_IsAccepted(string type)
    {
        var criteria = type switch
        {
            "choice" => ", \"criteria\": {\"a\": \"A\"}",
            "score" => ", \"criteria\": [\"low\", \"high\"]",
            _ => "",
        };
        var body = "{ \"type\": \"" + type + "\", \"instructions\": \"x\"" + criteria + " }";

        var json = Request($$"""
            "model": "laya-en", "state": null,
            "questions": { "q": {{body}} }
            """);

        var problems = RequestValidator.Validate(json);

        Assert.Empty(problems);
    }

    [Fact]
    public void Question_Instructions_Required()
    {
        var json = Request("""
            "model": "laya-en", "state": null,
            "questions": { "q": { "type": "noul" } }
            """);

        var problems = RequestValidator.Validate(json);

        Assert.Contains(problems, p => p.Path == "questions.q.instructions");
    }

    [Fact]
    public void Question_Instructions_AsNumber_IsInvalid()
    {
        var json = Request("""
            "model": "laya-en", "state": null,
            "questions": { "q": { "type": "noul", "instructions": 42 } }
            """);

        var problems = RequestValidator.Validate(json);

        Assert.Contains(problems, p => p.Path == "questions.q.instructions");
    }

    [Theory]
    [InlineData("\"instructions\": \"text\"")]
    [InlineData("\"instructions\": {\"a\": \"b\"}")]
    [InlineData("\"instructions\": [\"a\", \"b\"]")]
    public void Question_Instructions_StringObjectOrArray_IsValid(string instructions)
    {
        var json = Request($$"""
            "model": "laya-en", "state": null,
            "questions": { "q": { "type": "noul", {{instructions}} } }
            """);

        var problems = RequestValidator.Validate(json);

        Assert.Empty(problems);
    }

    [Fact]
    public void Question_UnknownField_IsInvalid()
    {
        var json = Request("""
            "model": "laya-en", "state": null,
            "questions": { "q": { "type": "noul", "instructions": "x", "bogus": 1 } }
            """);

        var problems = RequestValidator.Validate(json);

        Assert.Contains(problems, p => p.Path == "questions.q.bogus");
    }

    [Fact]
    public void ChoiceCriteria_Required()
    {
        var json = Request("""
            "model": "laya-en", "state": null,
            "questions": { "q": { "type": "choice", "instructions": "x" } }
            """);

        var problems = RequestValidator.Validate(json);

        Assert.Contains(problems, p => p.Path == "questions.q.criteria");
    }

    [Fact]
    public void ChoiceCriteria_MustBeAnObject()
    {
        var json = Request("""
            "model": "laya-en", "state": null,
            "questions": { "q": { "type": "choice", "instructions": "x", "criteria": ["a"] } }
            """);

        var problems = RequestValidator.Validate(json);

        Assert.Contains(problems, p => p.Path == "questions.q.criteria");
    }

    [Fact]
    public void ChoiceCriteria_ZeroOptions_IsInvalid()
    {
        var json = Request("""
            "model": "laya-en", "state": null,
            "questions": { "q": { "type": "choice", "instructions": "x", "criteria": {} } }
            """);

        var problems = RequestValidator.Validate(json);

        Assert.Contains(problems, p => p.Path == "questions.q.criteria");
    }

    [Fact]
    public void ChoiceCriteria_OneOption_IsValid()
    {
        var json = Request("""
            "model": "laya-en", "state": null,
            "questions": { "q": { "type": "choice", "instructions": "x", "criteria": {"a": "A"} } }
            """);

        Assert.Empty(RequestValidator.Validate(json));
    }

    [Fact]
    public void ChoiceCriteria_255Options_IsValid()
    {
        var json = Request(
            "\"model\": \"laya-en\", \"state\": null, \"questions\": { \"q\": { \"type\": \"choice\", \"instructions\": \"x\", \"criteria\": "
            + BuildOptions(255) + " } }");

        Assert.Empty(RequestValidator.Validate(json));
    }

    [Fact]
    public void ChoiceCriteria_256Options_IsInvalid()
    {
        var json = Request(
            "\"model\": \"laya-en\", \"state\": null, \"questions\": { \"q\": { \"type\": \"choice\", \"instructions\": \"x\", \"criteria\": "
            + BuildOptions(256) + " } }");

        var problems = RequestValidator.Validate(json);

        Assert.Contains(problems, p => p.Path == "questions.q.criteria");
    }

    private static string BuildOptions(int count)
    {
        var entries = Enumerable.Range(0, count).Select(i => $"\"o{i}\": \"Option {i}\"");
        return "{" + string.Join(",", entries) + "}";
    }

    [Theory]
    [InlineData("\"text\"")]
    [InlineData("{\"a\": \"b\"}")]
    [InlineData("[\"a\"]")]
    [InlineData("null")]
    public void ChoiceCriteria_ValueMayBeStringObjectArrayOrNull(string value)
    {
        var json = Request($$"""
            "model": "laya-en", "state": null,
            "questions": { "q": { "type": "choice", "instructions": "x", "criteria": {"a": {{value}} } } }
            """);

        Assert.Empty(RequestValidator.Validate(json));
    }

    [Fact]
    public void ChoiceCriteria_ValueMayNotBeANumber()
    {
        var json = Request("""
            "model": "laya-en", "state": null,
            "questions": { "q": { "type": "choice", "instructions": "x", "criteria": {"a": 1} } }
            """);

        var problems = RequestValidator.Validate(json);

        Assert.Contains(problems, p => p.Path == "questions.q.criteria.a");
    }

    [Fact]
    public void ScoreCriteria_Required()
    {
        var json = Request("""
            "model": "laya-en", "state": null,
            "questions": { "q": { "type": "score", "instructions": "x" } }
            """);

        var problems = RequestValidator.Validate(json);

        Assert.Contains(problems, p => p.Path == "questions.q.criteria");
    }

    [Fact]
    public void ScoreCriteria_MustBeAnArray()
    {
        var json = Request("""
            "model": "laya-en", "state": null,
            "questions": { "q": { "type": "score", "instructions": "x", "criteria": {} } }
            """);

        var problems = RequestValidator.Validate(json);

        Assert.Contains(problems, p => p.Path == "questions.q.criteria");
    }

    [Fact]
    public void ScoreCriteria_OneLevel_IsInvalid()
    {
        var json = Request("""
            "model": "laya-en", "state": null,
            "questions": { "q": { "type": "score", "instructions": "x", "criteria": ["low"] } }
            """);

        var problems = RequestValidator.Validate(json);

        Assert.Contains(problems, p => p.Path == "questions.q.criteria");
    }

    [Fact]
    public void ScoreCriteria_TwoLevels_IsValid()
    {
        var json = Request("""
            "model": "laya-en", "state": null,
            "questions": { "q": { "type": "score", "instructions": "x", "criteria": ["low", "high"] } }
            """);

        Assert.Empty(RequestValidator.Validate(json));
    }

    [Fact]
    public void ScoreCriteria_TenLevels_IsValid()
    {
        var levels = string.Join(",", Enumerable.Range(0, 10).Select(i => $"\"l{i}\""));
        var json = Request($$"""
            "model": "laya-en", "state": null,
            "questions": { "q": { "type": "score", "instructions": "x", "criteria": [{{levels}}] } }
            """);

        Assert.Empty(RequestValidator.Validate(json));
    }

    [Fact]
    public void ScoreCriteria_ElevenLevels_IsInvalid()
    {
        var levels = string.Join(",", Enumerable.Range(0, 11).Select(i => $"\"l{i}\""));
        var json = Request($$"""
            "model": "laya-en", "state": null,
            "questions": { "q": { "type": "score", "instructions": "x", "criteria": [{{levels}}] } }
            """);

        var problems = RequestValidator.Validate(json);

        Assert.Contains(problems, p => p.Path == "questions.q.criteria");
    }

    [Theory]
    [InlineData("\"text\"")]
    [InlineData("{\"a\": \"b\"}")]
    [InlineData("[\"a\"]")]
    public void ScoreCriteria_ItemMayBeStringObjectOrArray(string item)
    {
        var json = Request($$"""
            "model": "laya-en", "state": null,
            "questions": { "q": { "type": "score", "instructions": "x", "criteria": [{{item}}, "high"] } }
            """);

        Assert.Empty(RequestValidator.Validate(json));
    }

    [Fact]
    public void ScoreCriteria_ItemMayNotBeNull()
    {
        var json = Request("""
            "model": "laya-en", "state": null,
            "questions": { "q": { "type": "score", "instructions": "x", "criteria": [null, "high"] } }
            """);

        var problems = RequestValidator.Validate(json);

        Assert.Contains(problems, p => p.Path == "questions.q.criteria[0]");
    }

    [Fact]
    public void NoulCriteria_IsOptional()
    {
        var json = Request("""
            "model": "laya-en", "state": null,
            "questions": { "q": { "type": "noul", "instructions": "x" } }
            """);

        Assert.Empty(RequestValidator.Validate(json));
    }

    [Theory]
    [InlineData("True")]
    [InlineData("FALSE")]
    [InlineData("true")]
    [InlineData("False")]
    public void NoulCriteria_KeyCasing_IsAccepted(string key)
    {
        var json = Request($$"""
            "model": "laya-en", "state": null,
            "questions": { "q": { "type": "noul", "instructions": "x", "criteria": { "{{key}}": "d" } } }
            """);

        Assert.Empty(RequestValidator.Validate(json));
    }

    [Fact]
    public void NoulCriteria_UnknownKey_IsInvalid()
    {
        var json = Request("""
            "model": "laya-en", "state": null,
            "questions": { "q": { "type": "noul", "instructions": "x", "criteria": { "maybe": "d" } } }
            """);

        var problems = RequestValidator.Validate(json);

        Assert.Contains(problems, p => p.Path == "questions.q.criteria.maybe");
    }

    [Fact]
    public void TopLevel_UnknownField_IsInvalid()
    {
        var json = Request($"""
            "model": "laya-en", "state": null, {ValidQuestions}, "bogus": 1
            """);

        var problems = RequestValidator.Validate(json);

        Assert.Contains(problems, p => p.Path == "bogus");
    }

    [Fact]
    public void TopLevel_XTauField_IsAllowed()
    {
        var json = Request($"""
            "model": "laya-en", "state": null, {ValidQuestions}, "x-tau-raw": true
            """);

        Assert.Empty(RequestValidator.Validate(json));
    }

    [Fact]
    public void CollectsMultipleProblemsAtOnce()
    {
        var json = Request("""
            "state": null,
            "questions": { "q": { "type": "banana" } }
            """);

        var problems = RequestValidator.Validate(json);

        Assert.True(problems.Count >= 3);
        Assert.Contains(problems, p => p.Path == "model");
        Assert.Contains(problems, p => p.Path == "questions.q.type");
        Assert.Contains(problems, p => p.Path == "questions.q.instructions");
    }
}
