using System.Text.Json.Nodes;
using Tau.Inference.Routing;

namespace Tau.Inference.Tests.Routing;

/// <summary>
/// Every state in <c>routes.jsonl</c> must analyse and route in C# exactly as <c>laya.lang.analyse</c> and
/// <c>laya.router.Router._route</c> did in Python (laya 0.3.20).
/// </summary>
[Trait("Category", "Parity")]
public sealed class RoutingParityTests
{
    private static readonly HashSet<string> AllInstalled =
        [ModelRouter.LayaEnglish, ModelRouter.LayaMultilingual, ModelRouter.LayaTypedDecisions, ModelRouter.Von];

    public static TheoryData<string> FixtureIds()
    {
        var data = new TheoryData<string>();
        foreach (var id in RoutingFixtures.Rows.Keys)
        {
            data.Add(id);
        }

        return data;
    }

    [Fact]
    public void Fixture_has_at_least_250_states_and_a_matching_header()
    {
        var header = RoutingFixtures.Header;
        Assert.True(header["header"]!.GetValue<bool>());
        Assert.Equal("0.3.20", header["laya"]!.GetValue<string>());
        Assert.Equal(RoutingFixtures.Rows.Count, header["count"]!.GetValue<int>());
        Assert.True(RoutingFixtures.Rows.Count >= 250, $"only {RoutingFixtures.Rows.Count} states");
    }

    [Fact]
    public void Unicode_tables_come_from_the_fixture_interpreter()
    {
        var header = RoutingFixtures.Header;
        Assert.Equal(PyUnicodeTables.PythonVersion, header["python"]!.GetValue<string>());
        Assert.Equal(PyUnicodeTables.UnidataVersion, header["unidata"]!.GetValue<string>());
    }

    [Fact]
    public void Stopword_lists_diacritics_and_shared_words_match_the_reference_verbatim()
    {
        var header = RoutingFixtures.Header;
        var stop = header["stop"]!.AsObject();
        Assert.Equal(stop.Select(kv => kv.Key), LayaLangData.Stop.Select(s => s.Lang));
        foreach (var (lang, words) in LayaLangData.Stop)
        {
            var expected = stop[lang]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();
            Assert.Equal(expected, words.Order(StringComparer.Ordinal).ToArray());
        }

        var diacritics = header["diacritics"]!.AsArray().Select(n => char.ConvertToUtf32(n!.GetValue<string>(), 0)).Order();
        Assert.Equal(diacritics, LayaLangData.NonEnDiacritics.Order());

        var shared = header["shared"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();
        Assert.Equal(shared, LayaLangData.SharedWords.Order(StringComparer.Ordinal).ToArray());
    }

    [Theory]
    [MemberData(nameof(FixtureIds))]
    public void Analyse_matches_the_reference(string id)
    {
        var row = RoutingFixtures.Rows[id];
        var expected = row["analyse"]!.AsObject();
        var actual = ScriptAnalyser.Analyse(row["state"]);

        Assert.Equal(expected["script"]!.GetValue<string>(), actual.Script);
        Assert.Equal(expected["language"]?.GetValue<string>(), actual.Language);
        Assert.Equal(expected["is_english"]!.GetValue<bool>(), actual.IsEnglish);
        Assert.Equal(expected["language_undecided"]!.GetValue<bool>(), actual.LanguageUndecided);
        Assert.Equal(expected["diacritic_rate"]!.GetValue<double>(), actual.DiacriticRate);
        Assert.Equal(expected["non_latin_fraction"]!.GetValue<double>(), actual.NonLatinFraction);

        var profile = expected["script_profile"]!.AsObject().Select(kv => (kv.Key, kv.Value!.GetValue<double>())).ToArray();
        Assert.Equal(profile, actual.ScriptProfile.Select(kv => (kv.Key, kv.Value)).ToArray());
    }

    [Theory]
    [MemberData(nameof(FixtureIds))]
    public void Router_picks_the_reference_checkpoint_for_every_auto_alias(string id)
    {
        var row = RoutingFixtures.Rows[id];
        var route = row["route"]!.AsObject();
        var expectedModel = route["model"]!.GetValue<string>() switch
        {
            "english" => ModelRouter.LayaEnglish,
            "multilingual" => ModelRouter.LayaMultilingual,
            var other => throw new InvalidOperationException("unexpected reference model " + other),
        };

        var router = new ModelRouter();
        foreach (var alias in new[] { "auto", "tau-auto", "jev-latest", "JEV-1.0", null })
        {
            var decision = router.Resolve(alias, row["state"], AllInstalled);
            Assert.Equal(expectedModel, decision.ModelId);
            Assert.Equal(route["reason"]!.GetValue<string>(), decision.Reason);
            Assert.True(decision.Auto);
        }
    }

    [Fact]
    public void Analyse_leaves_the_state_untouched_and_is_repeatable()
    {
        var row = RoutingFixtures.Rows.Values.First(r => r["state"] is JsonObject);
        var before = row["state"]!.ToJsonString();
        var a = ScriptAnalyser.Analyse(row["state"]);
        var b = ScriptAnalyser.Analyse(row["state"]);
        Assert.Equal(before, row["state"]!.ToJsonString());
        Assert.Equal(a.Script, b.Script);
        Assert.Equal(a.ScriptProfile, b.ScriptProfile);
    }
}
