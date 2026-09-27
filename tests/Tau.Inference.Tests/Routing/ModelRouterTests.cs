using System.Text.Json.Nodes;
using Tau.Inference.Routing;

namespace Tau.Inference.Tests.Routing;

public sealed class ModelRouterTests
{
    private static readonly HashSet<string> AllInstalled =
        [ModelRouter.LayaEnglish, ModelRouter.LayaMultilingual, ModelRouter.LayaTypedDecisions, ModelRouter.Von];

    private static readonly JsonNode English = JsonValue.Create("I was charged twice for my order and I want a refund")!;
    private static readonly JsonNode German = JsonValue.Create("Mein Konto wurde zweimal belastet und ich möchte das Geld zurück")!;
    private static readonly JsonNode Chinese = JsonValue.Create("我的卡被扣了两次款，请退款。")!;

    [Theory]
    [InlineData("laya-en")]
    [InlineData("laya-multilingual")]
    [InlineData("laya-typed-decisions")]
    [InlineData("von-1.2.0")]
    public void Explicit_installed_id_is_used_whatever_the_state(string id)
    {
        foreach (var state in new[] { English, German, Chinese, null })
        {
            var decision = new ModelRouter().Resolve(id, state, AllInstalled);
            Assert.Equal(id, decision.ModelId);
            Assert.False(decision.Auto);
        }
    }

    [Theory]
    [InlineData("  LAYA-EN ", "laya-en")]
    [InlineData("Laya-Multilingual", "laya-multilingual")]
    public void Explicit_ids_are_matched_case_insensitively_after_trimming(string requested, string expected)
    {
        Assert.Equal(expected, new ModelRouter().Resolve(requested, Chinese, AllInstalled).ModelId);
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("tau-auto")]
    [InlineData("jev-latest")]
    [InlineData("JEV-LATEST")]
    [InlineData("jev-2")]
    [InlineData("jev-1.4.0-beta")]
    [InlineData("Jev-anything")]
    [InlineData("jev-")]
    [InlineData(" Auto ")]
    [InlineData(null)]
    public void Aliases_route_by_detection(string? alias)
    {
        var router = new ModelRouter();
        Assert.Equal(ModelRouter.LayaEnglish, router.Resolve(alias, English, AllInstalled).ModelId);
        Assert.Equal(ModelRouter.LayaMultilingual, router.Resolve(alias, German, AllInstalled).ModelId);
        Assert.Equal(ModelRouter.LayaMultilingual, router.Resolve(alias, Chinese, AllInstalled).ModelId);
        Assert.True(router.Resolve(alias, German, AllInstalled).Auto);
    }

    [Fact]
    public void States_without_letters_take_the_english_default()
    {
        var decision = new ModelRouter().Resolve("auto", JsonValue.Create(12345), AllInstalled);
        Assert.Equal(ModelRouter.LayaEnglish, decision.ModelId);
        Assert.Equal("no letters detected in state; using default (english)", decision.Reason);
    }

    [Fact]
    public void Typed_decisions_is_never_chosen_automatically()
    {
        var router = new ModelRouter();
        foreach (var row in RoutingFixtures.Rows.Values)
        {
            var decision = router.Resolve("auto", row["state"], AllInstalled);
            Assert.NotEqual(ModelRouter.LayaTypedDecisions, decision.ModelId);
            Assert.NotEqual(ModelRouter.Von, decision.ModelId);
        }
    }

    [Theory]
    [InlineData("gpt-4")]
    [InlineData("laya")]
    [InlineData("english")]
    [InlineData("tau")]
    [InlineData("")]
    [InlineData("jev")]
    public void Unknown_model_lists_installed_ids_and_aliases(string requested)
    {
        var ex = Assert.Throws<UnknownModelException>(() => new ModelRouter().Resolve(requested, English, AllInstalled));
        Assert.Equal(requested, ex.Requested);
        Assert.Equal(AllInstalled.Order(StringComparer.Ordinal), ex.Installed);
        Assert.Contains("auto", ex.Aliases);
        Assert.Contains("jev-*", ex.Aliases);
        foreach (var id in AllInstalled)
        {
            Assert.Contains(id, ex.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Known_id_that_is_not_installed_is_unknown()
    {
        HashSet<string> installed = [ModelRouter.LayaEnglish, ModelRouter.LayaMultilingual];
        var ex = Assert.Throws<UnknownModelException>(() => new ModelRouter().Resolve("von-1.2.0", English, installed));
        Assert.Equal(["laya-en", "laya-multilingual"], ex.Installed);
    }

    [Fact]
    public void Auto_route_to_a_missing_checkpoint_fails_loudly()
    {
        HashSet<string> installed = [ModelRouter.LayaEnglish];
        Assert.Equal(ModelRouter.LayaEnglish, new ModelRouter().Resolve("auto", English, installed).ModelId);
        Assert.Throws<InvalidOperationException>(() => new ModelRouter().Resolve("auto", Chinese, installed));
    }

    [Fact]
    public void Configured_aliases_replace_the_defaults_but_jev_star_still_applies()
    {
        var router = new ModelRouter(["Pick-For-Me"]);
        Assert.True(router.Resolve("pick-for-me", German, AllInstalled).Auto);
        Assert.True(router.Resolve("jev-latest", German, AllInstalled).Auto);
        Assert.Throws<UnknownModelException>(() => router.Resolve("tau-auto", German, AllInstalled));
    }

    [Fact]
    public void Reasons_follow_the_reference_wording()
    {
        var router = new ModelRouter();
        Assert.Equal("English Latin text", router.Resolve("auto", English, AllInstalled).Reason);
        Assert.Equal("Latin script but language looks like 'de', not English", router.Resolve("auto", German, AllInstalled).Reason);
        Assert.Equal("non-Latin script (han, 100% of letters); the English checkpoint cannot read it",
            router.Resolve("auto", Chinese, AllInstalled).Reason);
    }
}
