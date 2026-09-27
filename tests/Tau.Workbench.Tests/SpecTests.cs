using Tau.Calibration;
using Tau.Workbench.Spec;

namespace Tau.Workbench.Tests;

public sealed class SpecTests
{
    private static string RepoRoot => DecisionSpec.FindRepoRoot(AppContext.BaseDirectory)!;

    [Theory]
    [InlineData("banking77", QuestionType.Choice, 77)]
    [InlineData("support-tickets", QuestionType.Score, 5)]
    public void TheCommittedExampleSpecsLoad(string example, QuestionType type, int classes)
    {
        var spec = DecisionSpec.Load(Path.Combine(RepoRoot, "examples", example, "decision.yaml"));
        Assert.Equal(example, spec.Name);
        Assert.Equal(type, spec.Question.Type);
        Assert.Equal(classes, spec.Question.Classes.Count);
        Assert.Equal("claude-opus-5-5", spec.Pricing.Headline);
        Assert.NotNull(spec.Pricing.GbpPerUsd);
        Assert.Equal(1.3, spec.Pricing.TokenizerFactor);
        Assert.Equal(4, spec.Pricing.Rows.Count);
        Assert.Equal(200, spec.Frontier.AltSubset);
    }

    [Fact]
    public void ChoiceSpecKeepsOptionOrderAndResolvesPaths()
    {
        using var repo = TestRepo.Create(classes: 5);
        var spec = repo.Spec;
        Assert.Equal(["a", "b", "c", "d", "e"], spec.Question.Classes);
        Assert.Equal(spec.Question.Classes, spec.Question.AllowedAnswers);
        Assert.Equal(Path.Combine(repo.Root, "data", "demo", "heldout.jsonl"), spec.SplitPath("heldout"));
        Assert.Equal(Path.Combine(repo.SpecDir, "dataset.manifest.json"), spec.ManifestPath);
        Assert.Equal(Path.Combine(repo.SpecDir, "frontier", "pending", "v1"), spec.PendingDirectory("v1"));
        Assert.Equal(Path.Combine(repo.SpecDir, "runs", "model-a", "heldout.raw.jsonl"), spec.RunPath("model-a", "heldout", "raw"));
        Assert.Equal(Path.Combine(repo.SpecDir, "calibrators", "model-a"), spec.CalibratorsDirectory("model-a"));
        Assert.Equal("Demo decision", spec.Title);
        Assert.Equal(new Uri("http://localhost:18093"), spec.Endpoint);
        Assert.Equal(0.75, spec.Pricing.GbpPerUsd);
        Assert.Equal("frontier-x", spec.Pricing.Headline);
    }

    [Fact]
    public void ScoreLevelsAreIndexedFromZero()
    {
        using var repo = TestRepo.Create(type: "score", classes: 5);
        var q = repo.Spec.Question;
        Assert.Equal(["0", "1", "2", "3", "4"], q.Classes);
        Assert.Equal("level 3", q.Describe("3"));
        Assert.Equal(3, q.ClassIndex("3"));
        Assert.Null(q.ClassIndex("5"));
    }

    [Fact]
    public void NoulClassesAreFalseThenTrueAndLabelsAreCaseInsensitive()
    {
        using var repo = TestRepo.Create(type: "noul");
        var q = repo.Spec.Question;
        Assert.Equal(["false", "true"], q.Classes);
        Assert.Equal(["true", "false"], q.AllowedAnswers);
        Assert.Equal(1, q.ClassIndex("TRUE"));
        Assert.Equal(0, q.ClassIndex(" false "));
        Assert.Null(q.ClassIndex("yes"));
    }

    [Theory]
    [InlineData("name: demo", "name: ../escape", "name '../escape'")]
    [InlineData("  type: choice", "  type: ranking", "question.type 'ranking'")]
    [InlineData("models: [model-a]", "models: [../../etc]", "models entry '../../etc'")]
    [InlineData("models: [model-a]", "models: []", "at least one model")]
    [InlineData("models: [model-a]", "models: [m, M]", "more than once")]
    [InlineData("  target_error: 0.1", "  target_error: 1.5", "target_error must be between 0 and 1")]
    [InlineData("endpoint: http://localhost:18093", "endpoint: ftp://x", "absolute http(s) URL")]
    [InlineData("  headline: frontier-x", "  headline: nobody", "headline model 'nobody'")]
    [InlineData("    cheap-y: [1, 5]", "    cheap-y: [1]", "pricing.usd_per_mtok.cheap-y")]
    [InlineData("    cheap-y: [1, 5]", "    cheap-y: [-1, 5]", "pricing.usd_per_mtok.cheap-y")]
    [InlineData("  basis_date: \"2026-09-27\"", "  basis_date: \"27/09/2026\"", "yyyy-MM-dd")]
    [InlineData("  tokenizer_factor: 1.3", "  tokenizer_factor: 0", "tokenizer_factor must be positive")]
    [InlineData("  prompt_version: v1", "  prompt_version: v9", "prompt version 'v9' is unknown")]
    [InlineData("  alt_prompt_version: v1-alt", "  alt_prompt_version: v1", "must differ")]
    [InlineData("  alt_subset: 20", "  alt_subset: 5000", "alt_subset")]
    [InlineData("  dataset: demo", "  dataset: ../demo", "data.dataset '../demo'")]
    [InlineData("title: \"Demo decision\"", "colour: blue", "unknown key 'colour'")]
    [InlineData("  text_field: text", "  txt_field: text", "unknown key 'data.txt_field'")]
    public void InvalidSpecsAreRejectedWithTheProblemNamed(string find, string replace, string expected)
    {
        using var repo = TestRepo.Create();
        var yaml = File.ReadAllText(repo.SpecPath);
        Assert.Contains(find, yaml, StringComparison.Ordinal);
        repo.WriteSpec(yaml.Replace(find, replace, StringComparison.Ordinal));
        var e = Assert.Throws<SpecValidationException>(() => DecisionSpec.Load(repo.SpecPath));
        Assert.Contains(e.Problems, p => p.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void EveryProblemIsReportedAtOnce()
    {
        using var repo = TestRepo.Create();
        var yaml = File.ReadAllText(repo.SpecPath).Replace("type: choice", "type: nope", StringComparison.Ordinal).Replace("target_error: 0.1", "target_error: 2", StringComparison.Ordinal);
        repo.WriteSpec(yaml);
        var e = Assert.Throws<SpecValidationException>(() => DecisionSpec.Load(repo.SpecPath));
        Assert.True(e.Problems.Count >= 2);
    }

    [Theory]
    [InlineData("choice", 1, "2 to 255 options")]
    [InlineData("score", 11, "2 to 10 levels")]
    public void OptionCountsAreChecked(string type, int classes, string expected)
    {
        using var repo = TestRepo.Create(type: type, classes: classes);
        var e = Assert.Throws<SpecValidationException>(() => DecisionSpec.Load(repo.SpecPath));
        Assert.Contains(e.Problems, p => p.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateChoiceKeysAreRejected()
    {
        using var repo = TestRepo.Create();
        repo.WriteSpec(File.ReadAllText(repo.SpecPath).Replace("    b: ", "    a: ", StringComparison.Ordinal));
        Assert.Throws<SpecValidationException>(() => DecisionSpec.Load(repo.SpecPath));
    }

    [Fact]
    public void MissingSectionsAreNamed()
    {
        using var repo = TestRepo.Create();
        repo.WriteSpec("name: demo\n");
        var e = Assert.Throws<SpecValidationException>(() => DecisionSpec.Load(repo.SpecPath));
        foreach (var section in new[] { "question", "data", "frontier", "pricing" })
        {
            Assert.Contains(e.Problems, p => p.Contains($"'{section}'", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void YamlSyntaxErrorsAndEmptyFilesAreRejected()
    {
        using var repo = TestRepo.Create();
        repo.WriteSpec("name: [unclosed\n");
        Assert.Contains("YAML syntax error", Assert.Throws<SpecValidationException>(() => DecisionSpec.Load(repo.SpecPath)).Message, StringComparison.Ordinal);
        repo.WriteSpec("");
        Assert.Throws<SpecValidationException>(() => DecisionSpec.Load(repo.SpecPath));
        repo.WriteSpec("- a\n- b\n");
        Assert.Throws<SpecValidationException>(() => DecisionSpec.Load(repo.SpecPath));
    }

    [Fact]
    public void AMissingSpecFileIsAWorkbenchError()
    {
        var e = Assert.Throws<WorkbenchException>(() => DecisionSpec.Load(Path.Combine(Path.GetTempPath(), "no-such-spec.yaml")));
        Assert.Contains("does not exist", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ZeroRatesMeanUnknownNotFree()
    {
        using var repo = TestRepo.Create(pricing: TestRepo.DefaultPricing.Replace("gbp_per_usd: 0.75", "gbp_per_usd: 0", StringComparison.Ordinal)
            .Replace("electricity_gbp_per_kwh: 0.25", "electricity_gbp_per_kwh: 0.0", StringComparison.Ordinal));
        Assert.Null(repo.Spec.Pricing.GbpPerUsd);
        Assert.Null(repo.Spec.Pricing.ElectricityGbpPerKwh);
    }

    [Fact]
    public void HeadlineDefaultsToTheFrontierModel()
    {
        using var repo = TestRepo.Create(pricing: TestRepo.DefaultPricing.Replace("  headline: frontier-x\n", "", StringComparison.Ordinal));
        Assert.Equal("frontier-x", repo.Spec.Pricing.Headline);
    }

    [Fact]
    public void EndpointIsOptional()
    {
        using var repo = TestRepo.Create();
        repo.WriteSpec(File.ReadAllText(repo.SpecPath).Replace("endpoint: http://localhost:18093\n", "", StringComparison.Ordinal));
        Assert.Null(repo.Spec.Endpoint);
    }
}
