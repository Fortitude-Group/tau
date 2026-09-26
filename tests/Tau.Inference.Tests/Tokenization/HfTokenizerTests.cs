using System.Text;
using System.Text.Json;
using Tau.Inference.Tokenization;
using Tokenizers.DotNet;

namespace Tau.Inference.Tests.Tokenization;

/// <summary>Behaviour of <see cref="HfTokenizer"/> beyond id parity.</summary>
public class HfTokenizerTests
{
    private static readonly string LayaEn = TestData.Path("Tokenization", "data", "laya-en", "tokenizer.json");
    private static readonly string Von = TestData.Path("Tokenization", "data", "von-1.2.0", "tokenizer.json");

    [Fact]
    public void Reads_special_tokens_from_the_config()
    {
        using var tok = HfTokenizer.Load(LayaEn);
        Assert.Equal(("[CLS]", 50281), (tok.ClsToken, tok.ClsId));
        Assert.Equal(("[SEP]", 50282), (tok.SepToken, tok.SepId));
        Assert.Equal(("[MASK]", 50284), (tok.MaskToken, tok.MaskId));
        Assert.Equal(("[PAD]", 50283), (tok.PadToken, tok.PadId));
    }

    [Fact]
    public void Adds_no_special_tokens_but_matches_them_inside_text()
    {
        using var tok = HfTokenizer.Load(LayaEn);
        Assert.Empty(tok.Encode(string.Empty));
        var ids = tok.Encode("a [SEP] b [MASK]");
        Assert.DoesNotContain(tok.ClsId, ids);
        Assert.Equal(1, ids.Count(i => i == tok.SepId));
        Assert.Equal(1, ids.Count(i => i == tok.MaskId));
    }

    [Fact]
    public void Does_not_apply_the_files_own_truncation()
    {
        // Von's tokenizer.json truncates at 8192 tokens; transformers does not for a plain call, nor do we.
        using var tok = HfTokenizer.Load(Von);
        var text = string.Join(' ', Enumerable.Repeat("refund", 9000));
        Assert.True(tok.Encode(text).Length > 8192);
    }

    [Fact]
    public void EncodeTruncated_keeps_the_first_ids()
    {
        using var tok = HfTokenizer.Load(LayaEn);
        var all = tok.Encode("The quick brown fox jumps over the lazy dog.");
        Assert.Equal(all[..3], tok.EncodeTruncated("The quick brown fox jumps over the lazy dog.", 3));
        Assert.Equal(all, tok.EncodeTruncated("The quick brown fox jumps over the lazy dog.", 1000));
        Assert.Equal(all, tok.EncodeTruncated("The quick brown fox jumps over the lazy dog.", all.Length));
        Assert.Empty(tok.EncodeTruncated("The quick brown fox", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => tok.EncodeTruncated("x", -1));
    }

    [Fact]
    public void Is_safe_to_share_across_threads()
    {
        using var tok = HfTokenizer.Load(LayaEn);
        var texts = Enumerable.Range(0, 200).Select(i => $"ticket {i}: customer {i * 7} wants a refund 中文 {i}").ToArray();
        var serial = texts.Select(tok.Encode).ToArray();
        var parallel = new int[texts.Length][];
        Parallel.For(0, texts.Length, new ParallelOptions { MaxDegreeOfParallelism = 16 }, i => parallel[i] = tok.Encode(texts[i]));
        for (int i = 0; i < texts.Length; i++)
        {
            Assert.Equal(serial[i], parallel[i]);
        }
    }

    [Fact]
    public void Throws_after_dispose()
    {
        var tok = HfTokenizer.Load(LayaEn);
        tok.Dispose();
        tok.Dispose(); // idempotent
        Assert.Throws<ObjectDisposedException>(() => tok.Encode("x"));
    }

    [Fact]
    public void Rejects_bad_input()
    {
        Assert.Throws<FileNotFoundException>(() => HfTokenizer.Load(Path.Combine(Path.GetTempPath(), "no-such-dir", "tokenizer.json")));
        using var tok = HfTokenizer.Load(LayaEn);
        Assert.Throws<ArgumentNullException>(() => tok.Encode(null!));
    }

    [Fact]
    public void Fails_clearly_without_a_config()
    {
        var dir = Directory.CreateTempSubdirectory("tau-tok-");
        try
        {
            File.Copy(LayaEn, Path.Combine(dir.FullName, "tokenizer.json"));
            var ex = Assert.Throws<FileNotFoundException>(() => HfTokenizer.Load(Path.Combine(dir.FullName, "tokenizer.json")));
            Assert.Contains("tokenizer_config.json", ex.Message);

            File.WriteAllText(Path.Combine(dir.FullName, "tokenizer_config.json"), "{\"cls_token\": \"[CLS]\", \"sep_token\": \"[SEP]\", \"mask_token\": \"[MASK]\"}");
            var missing = Assert.Throws<InvalidDataException>(() => HfTokenizer.Load(Path.Combine(dir.FullName, "tokenizer.json")));
            Assert.Contains("pad_token", missing.Message);

            File.WriteAllText(Path.Combine(dir.FullName, "tokenizer_config.json"),
                "{\"cls_token\": {\"content\": \"[CLS]\"}, \"sep_token\": \"[SEP]\", \"mask_token\": \"[MASK]\", \"pad_token\": \"<nope>\"}");
            var unknown = Assert.Throws<InvalidDataException>(() => HfTokenizer.Load(Path.Combine(dir.FullName, "tokenizer.json")));
            Assert.Contains("<nope>", unknown.Message);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void NullOutTopLevelSections_replaces_only_the_named_top_level_values()
    {
        var json = Encoding.UTF8.GetBytes("{\"a\": {\"post_processor\": 1}, \"post_processor\": {\"x\": [1, {\"y\": \"}\"}]}, \"truncation\": null, \"b\": \"padding\", \"padding\": [1,2]}");
        var result = Encoding.UTF8.GetString(HfTokenizer.NullOutTopLevelSections(json, ["post_processor", "truncation", "padding"]));
        Assert.Equal("{\"a\": {\"post_processor\": 1}, \"post_processor\": null, \"truncation\": null, \"b\": \"padding\", \"padding\": null}", result);
        using var doc = JsonDocument.Parse(result);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("post_processor").ValueKind);
    }

    [Fact]
    public void Native_library_rejects_a_malformed_file()
    {
        var dir = Directory.CreateTempSubdirectory("tau-tok-");
        try
        {
            var added = string.Join(", ", new[] { "[CLS]", "[SEP]", "[MASK]", "[PAD]" }.Select((t, i) =>
                $"{{\"id\": {i}, \"content\": \"{t}\", \"single_word\": false, \"lstrip\": false, \"rstrip\": false, \"normalized\": false, \"special\": true}}"));
            File.WriteAllText(Path.Combine(dir.FullName, "tokenizer.json"), $"{{\"added_tokens\": [{added}], \"model\": {{\"type\": \"Nonsense\"}}}}");
            File.WriteAllText(Path.Combine(dir.FullName, "tokenizer_config.json"),
                "{\"cls_token\": \"[CLS]\", \"sep_token\": \"[SEP]\", \"mask_token\": \"[MASK]\", \"pad_token\": \"[PAD]\"}");
            Assert.Throws<TokenizerException>(() => HfTokenizer.Load(Path.Combine(dir.FullName, "tokenizer.json")));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
