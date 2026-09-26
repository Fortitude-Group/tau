using System.Text.Json;
using Tokenizers.DotNet;

namespace Tau.Inference.Tokenization;

/// <summary>
/// A Hugging Face <c>tokenizer.json</c> tokeniser, run by the same Rust <c>tokenizers</c> library the Python
/// reference uses (through Tokenizers.DotNet), so ids match by construction rather than by reimplementation.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Encode"/> is Python's <c>tok(text, add_special_tokens=False)["input_ids"]</c>. Tokenizers.DotNet
/// only exposes an encode that adds special tokens, applies the file's truncation and pads, so the file is
/// loaded with its <c>post_processor</c>, <c>truncation</c> and <c>padding</c> sections set to null. With no
/// post-processor the Rust library adds nothing, which is what <c>add_special_tokens=False</c> does; and
/// transformers turns truncation and padding off for a plain call, which is why they go too (Von's file ships
/// an 8192-token truncation that would otherwise cut long states). Added tokens such as <c>[SEP]</c> that
/// appear literally in the text are still matched, exactly as in Python.
/// </para>
/// <para>
/// The special-token strings come from <c>tokenizer_config.json</c> beside the file and are resolved to ids
/// through the file's added tokens, then its vocabulary.
/// </para>
/// <para>Thread-safe: calls into the native library are serialised.</para>
/// </remarks>
public sealed class HfTokenizer : IDisposable
{
    private static readonly string[] StrippedSections = ["post_processor", "truncation", "padding"];

    private readonly Tokenizer _tokenizer;
    private readonly Lock _gate = new();
    private bool _disposed;

    private HfTokenizer(Tokenizer tokenizer, SpecialTokens specials)
    {
        _tokenizer = tokenizer;
        ClsToken = specials.Cls.Token;
        ClsId = specials.Cls.Id;
        SepToken = specials.Sep.Token;
        SepId = specials.Sep.Id;
        MaskToken = specials.Mask.Token;
        MaskId = specials.Mask.Id;
        PadToken = specials.Pad.Token;
        PadId = specials.Pad.Id;
    }

    /// <summary>The classifier (sequence start) token string, e.g. <c>[CLS]</c> or <c>&lt;bos&gt;</c>.</summary>
    public string ClsToken { get; }

    /// <summary>The id of <see cref="ClsToken"/>.</summary>
    public int ClsId { get; }

    /// <summary>The separator token string, e.g. <c>[SEP]</c> or <c>&lt;eos&gt;</c>.</summary>
    public string SepToken { get; }

    /// <summary>The id of <see cref="SepToken"/>.</summary>
    public int SepId { get; }

    /// <summary>The mask token string, e.g. <c>[MASK]</c> or <c>&lt;mask&gt;</c>.</summary>
    public string MaskToken { get; }

    /// <summary>The id of <see cref="MaskToken"/>.</summary>
    public int MaskId { get; }

    /// <summary>The padding token string, e.g. <c>[PAD]</c> or <c>&lt;pad&gt;</c>.</summary>
    public string PadToken { get; }

    /// <summary>The id of <see cref="PadToken"/>.</summary>
    public int PadId { get; }

    /// <summary>
    /// Loads <c>tokenizer.json</c> and the <c>tokenizer_config.json</c> in the same directory.
    /// </summary>
    /// <param name="tokenizerJsonPath">Path to a Hugging Face <c>tokenizer.json</c>.</param>
    /// <returns>The loaded tokeniser.</returns>
    /// <exception cref="FileNotFoundException">Either file is missing.</exception>
    /// <exception cref="InvalidDataException">The config lacks a cls, sep, mask or pad token, or one is not in the vocabulary.</exception>
    /// <exception cref="TokenizerException">The native library rejected the file.</exception>
    public static HfTokenizer Load(string tokenizerJsonPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenizerJsonPath);
        var fullPath = Path.GetFullPath(tokenizerJsonPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"tokenizer.json not found: {fullPath}", fullPath);
        }

        var configPath = Path.Combine(Path.GetDirectoryName(fullPath)!, "tokenizer_config.json");
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"tokenizer_config.json not found beside {fullPath}", configPath);
        }

        var bytes = File.ReadAllBytes(fullPath);
        var specials = ResolveSpecialTokens(bytes, File.ReadAllBytes(configPath), fullPath);

        // The native library only loads from a path, so hand it a sanitised copy and delete it afterwards.
        var tempPath = Path.Combine(Path.GetTempPath(), $"tau-tokenizer-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllBytes(tempPath, NullOutTopLevelSections(bytes, StrippedSections));
            return new HfTokenizer(new Tokenizer(tempPath), specials);
        }
        finally
        {
            try
            {
                File.Delete(tempPath);
            }
            catch (IOException)
            {
                // Best effort: a leftover temp copy is harmless.
            }
        }
    }

    /// <summary>
    /// Token ids for <paramref name="text"/> with no special tokens added: Python's
    /// <c>tok(text, add_special_tokens=False)["input_ids"]</c>.
    /// </summary>
    /// <param name="text">The text to tokenise.</param>
    /// <returns>The ids, untruncated.</returns>
    public int[] Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        uint[] raw;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            raw = _tokenizer.Encode(text);
        }

        var ids = new int[raw.Length];
        for (int i = 0; i < raw.Length; i++)
        {
            ids[i] = checked((int)raw[i]);
        }

        return ids;
    }

    /// <summary>The first <paramref name="maxTokens"/> ids of <see cref="Encode"/> (all of them if fewer).</summary>
    /// <param name="text">The text to tokenise.</param>
    /// <param name="maxTokens">The most ids to return; zero or more.</param>
    /// <returns>The ids, truncated on the right.</returns>
    public int[] EncodeTruncated(string text, int maxTokens)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxTokens);
        var ids = Encode(text);
        return ids.Length <= maxTokens ? ids : ids[..maxTokens];
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _tokenizer.Dispose();
        }
    }

    /// <summary>Returns a copy of a JSON object document with the named top-level properties set to null.</summary>
    internal static byte[] NullOutTopLevelSections(byte[] json, IReadOnlyCollection<string> names)
    {
        var spans = new List<(int Start, int End)>();
        var reader = new Utf8JsonReader(json, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip });
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            throw new InvalidDataException("tokenizer.json is not a JSON object.");
        }

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            bool strip = names.Contains(reader.GetString()!);
            reader.Read();
            int start = checked((int)reader.TokenStartIndex);
            reader.Skip();
            if (strip)
            {
                spans.Add((start, checked((int)reader.BytesConsumed)));
            }
        }

        using var output = new MemoryStream(json.Length);
        int pos = 0;
        foreach (var (start, end) in spans)
        {
            output.Write(json, pos, start - pos);
            output.Write("null"u8);
            pos = end;
        }

        output.Write(json, pos, json.Length - pos);
        return output.ToArray();
    }

    private readonly record struct Special(string Token, int Id);

    private readonly record struct SpecialTokens(Special Cls, Special Sep, Special Mask, Special Pad);

    private static SpecialTokens ResolveSpecialTokens(byte[] tokenizerJson, byte[] configJson, string path)
    {
        using var tok = JsonDocument.Parse(tokenizerJson);
        using var cfg = JsonDocument.Parse(configJson);

        var added = new Dictionary<string, int>(StringComparer.Ordinal);
        if (tok.RootElement.TryGetProperty("added_tokens", out var addedTokens) && addedTokens.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in addedTokens.EnumerateArray())
            {
                added.TryAdd(t.GetProperty("content").GetString()!, t.GetProperty("id").GetInt32());
            }
        }

        JsonElement vocab = default;
        bool hasVocab = tok.RootElement.TryGetProperty("model", out var model) && model.TryGetProperty("vocab", out vocab);

        Special Resolve(string key)
        {
            if (!cfg.RootElement.TryGetProperty(key, out var v))
            {
                throw new InvalidDataException($"tokenizer_config.json beside {path} has no {key}.");
            }

            // A token is stored either as a string or as an AddedToken object with a "content" field.
            var token = v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Object when v.TryGetProperty("content", out var c) => c.GetString(),
                _ => null,
            } ?? throw new InvalidDataException($"tokenizer_config.json beside {path}: {key} is not a token string.");

            if (added.TryGetValue(token, out var id))
            {
                return new Special(token, id);
            }

            if (hasVocab && vocab.ValueKind == JsonValueKind.Object && vocab.TryGetProperty(token, out var vid))
            {
                return new Special(token, vid.GetInt32());
            }

            throw new InvalidDataException($"{path}: {key} '{token}' is neither an added token nor in the vocabulary.");
        }

        return new SpecialTokens(Resolve("cls_token"), Resolve("sep_token"), Resolve("mask_token"), Resolve("pad_token"));
    }
}
