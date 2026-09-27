using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Tau.Inference.Tokenization;

namespace Tau.Inference.Tests.Tokenization;

/// <summary>
/// Token ids from <see cref="HfTokenizer"/> against <c>AutoTokenizer(text, add_special_tokens=False)</c>, exactly,
/// over the fixtures written by <c>data/gen_tokenizer_fixtures.py</c>.
/// </summary>
public class HfTokenizerParityTests
{
    [Fact]
    [Trait("Category", "Parity")]
    public void Laya_english_matches_transformers() =>
        AssertParity(TestData.Path("Tokenization", "data", "laya-en", "tokenizer.json"), "laya-en");

    [Fact]
    [Trait("Category", "Parity")]
    public void Von_matches_transformers() =>
        AssertParity(TestData.Path("Tokenization", "data", "von-1.2.0", "tokenizer.json"), "von-1.2.0");

    [Fact]
    [Trait("Category", "Models")]
    [Trait("Category", "Parity")]
    public void Laya_multilingual_matches_transformers() =>
        AssertParity(TestData.RequireModelFile("src", "laya-multilingual", "tokenizer", "tokenizer.json"), "laya-multilingual");

    private static void AssertParity(string tokenizerJson, string model)
    {
        var (header, cases) = TestData.ReadJsonl(TestData.Path("Tokenization", "data", $"{model}.expected.jsonl"));

        // The fixture must describe this exact file, or a match proves nothing.
        var sha = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(tokenizerJson)));
        Assert.Equal(header["tokenizer_json_sha256"]!.GetValue<string>(), sha);
        Assert.True(cases.Count >= 100, $"{model}: only {cases.Count} cases");

        using var tok = HfTokenizer.Load(tokenizerJson);
        var ids = header["special_ids"]!.AsObject();
        Assert.Equal(ids["cls_id"]!.GetValue<int>(), tok.ClsId);
        Assert.Equal(ids["sep_id"]!.GetValue<int>(), tok.SepId);
        Assert.Equal(ids["mask_id"]!.GetValue<int>(), tok.MaskId);
        Assert.Equal(ids["pad_id"]!.GetValue<int>(), tok.PadId);
        Assert.Equal(header["special_tokens"]!["mask_token"]!.GetValue<string>(), tok.MaskToken);

        var failures = new StringBuilder();
        int failed = 0;
        foreach (var c in cases)
        {
            var text = c["text"]!.GetValue<string>();
            var expected = c["ids"]!.AsArray().Select(n => n!.GetValue<int>()).ToArray();
            var actual = tok.Encode(text);
            if (!expected.AsSpan().SequenceEqual(actual))
            {
                if (++failed <= 10)
                {
                    int at = 0;
                    while (at < Math.Min(expected.Length, actual.Length) && expected[at] == actual[at]) at++;
                    failures.AppendLine($"text: {JsonValue.Create(text.Length > 200 ? text[..200] + "..." : text)!.ToJsonString()}")
                            .AppendLine($"  lengths expected {expected.Length}, actual {actual.Length}; first difference at {at}")
                            .AppendLine($"  expected[{at}..]: {string.Join(",", expected.Skip(at).Take(12))}")
                            .AppendLine($"  actual[{at}..]:   {string.Join(",", actual.Skip(at).Take(12))}");
                }
            }
        }

        Assert.True(failed == 0, $"{model}: {failed} of {cases.Count} texts tokenise differently from transformers {header["transformers"]}:\n{failures}");
    }
}
