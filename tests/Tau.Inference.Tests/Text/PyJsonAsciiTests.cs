using System.Text.Json.Nodes;
using Tau.Inference.Text;

namespace Tau.Inference.Tests.Text;

/// <summary>
/// <see cref="PyJson.DumpsAscii"/> against CPython's <c>json.dumps(v, sort_keys=isinstance(v, dict))</c>, the way
/// von-sdk 1.2.3 turns structured instructions into text (fixture: <c>data/pyjson_ascii.jsonl</c>).
/// </summary>
public class PyJsonAsciiTests
{
    [Fact]
    [Trait("Category", "Parity")]
    public void DumpsAscii_matches_python_for_every_case()
    {
        var (header, cases) = TestData.ReadJsonl(TestData.Path("Text", "data", "pyjson_ascii.jsonl"));
        Assert.StartsWith("3.12.", header["python"]!.GetValue<string>());
        Assert.True(cases.Count >= 60);
        var failures = new List<string>();
        foreach (var c in cases)
        {
            var node = JsonNode.Parse(c["raw"]!.GetValue<string>());
            var actual = PyJson.DumpsAscii(node, c["sort_keys"]!.GetValue<bool>());
            var expected = c["expected"]!.GetValue<string>();
            if (actual != expected) failures.Add($"{c["raw"]}\n  expected {expected}\n  actual   {actual}");
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(5)));
    }

    [Fact]
    public void Sorts_keys_by_code_point_not_utf16_unit()
    {
        // U+E000 sorts before U+1F600 by code point, but after it by UTF-16 unit (0xD83D < 0xE000).
        var node = JsonNode.Parse("{\"\\ud83d\\ude00\": 1, \"\\ue000\": 2}");
        Assert.Equal("{\"\\ue000\": 2, \"\\ud83d\\ude00\": 1}", PyJson.DumpsAscii(node, sortKeys: true));
    }
}
