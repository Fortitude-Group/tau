using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Tau.Contract;
using Tau.Inference.Engine;
using Tau.Inference.Models;
using Tau.Inference.Text;
using Tau.Inference.Tokenization;

namespace Tau.Inference.Von;

/// <summary>What a Von row is for.</summary>
public enum VonRowKind
{
    /// <summary>A choice question.</summary>
    Choice,

    /// <summary>A score question.</summary>
    Score,

    /// <summary>A noul question against the real state.</summary>
    Noul,

    /// <summary>The same noul question against an empty state (zero-shot prior correction).</summary>
    NoulNull,
}

/// <summary>A Von question as the backend sees it.</summary>
/// <param name="Key">The question's name.</param>
/// <param name="Type">choice, score or noul.</param>
/// <param name="Labels">Choice keys, or score legend texts; empty for noul.</param>
/// <param name="Descriptions">Option texts packed after each [MASK].</param>
/// <param name="Instructions">Instructions as text.</param>
/// <param name="ExplicitNoulCriteria">For noul: whether true/false descriptions were given.</param>
public sealed record VonQuestion(string Key, string Type, IReadOnlyList<string> Labels, IReadOnlyList<string> Descriptions,
    string Instructions, bool ExplicitNoulCriteria);

/// <summary>One Von row.</summary>
/// <param name="Question">The question.</param>
/// <param name="Kind">What the row is for.</param>
/// <param name="InputIds">Token ids including [CLS] and the trailing [SEP].</param>
/// <param name="Markers">Positions of the option [MASK] markers.</param>
public sealed record VonRow(VonQuestion Question, VonRowKind Kind, int[] InputIds, int[] Markers);

/// <summary>
/// Builds Von's rows exactly as von-sdk 1.2.3's <c>OptionMarkerBackend</c> does: the state formatted by
/// <c>_format_state</c>, <c>pack_sequence</c> = <c>"{question} {state}".strip() [SEP] [MASK] opt0 [MASK] opt1 …</c>,
/// tokenised with special tokens, one row per question plus an empty-state row for zero-shot noul. The
/// order-invariant position ids and attention masks are built for a padded batch by <see cref="Collate"/>.
/// </summary>
public sealed class VonSequenceBuilder
{
    private static readonly Regex DigitRun = new(@"\d+", RegexOptions.CultureInvariant);
    private readonly HfTokenizer _tok;
    private readonly VonLimits _limits;
    private readonly VonPostProcessing _post;
    private readonly int _maxTokens;

    /// <summary>Creates a builder.</summary>
    /// <param name="tokenizer">The package's tokeniser.</param>
    /// <param name="limits">Von limits.</param>
    /// <param name="post">Von post-processing (for <c>digit_split</c>).</param>
    /// <param name="maxTokens">Tau's packed-sequence cap (a documented deviation; the reference allows the encoder's maximum).</param>
    public VonSequenceBuilder(HfTokenizer tokenizer, VonLimits limits, VonPostProcessing post, int maxTokens)
    {
        _tok = tokenizer;
        _limits = limits;
        _post = post;
        _maxTokens = Math.Min(maxTokens, limits.MaxPosition);
    }

    /// <summary>
    /// Validates and normalises a contract question the way von-sdk's pydantic types would, rejecting (422)
    /// the contract-valid shapes the reference itself rejects.
    /// </summary>
    /// <param name="key">The question's name.</param>
    /// <param name="q">The contract question.</param>
    /// <param name="problems">Collects rejections.</param>
    public static VonQuestion? Normalise(string key, Question q, List<ValidationProblem> problems)
    {
        var ins = StringifyInstructions(q.Instructions);
        switch (q)
        {
            case ChoiceQuestion c:
            {
                var keys = new List<string>();
                var descs = new List<string>();
                foreach (var (k, v) in c.Criteria)
                {
                    if (v is not null && !IsString(v))
                    {
                        problems.Add(new($"questions.{key}.criteria.{k}", "von-1.2.0 accepts only text (or null) option descriptions"));
                        continue;
                    }
                    var desc = v is null ? null : v.GetValue<string>();
                    keys.Add(k);
                    descs.Add(PyStr.Truthy(desc) ? PyStr.Strip(desc!) : PyStr.Strip(k));
                }
                return new VonQuestion(key, "choice", keys, descs, ins, false);
            }
            case ScoreQuestion s:
            {
                var descs = new List<string>();
                for (var i = 0; i < s.Criteria.Count; i++)
                {
                    var item = s.Criteria[i];
                    if (item is JsonObject o)
                    {
                        var what = o["what"] is { } w ? PyRepr.Str(w) : "";
                        var ex = new List<string>();
                        if (o["examples"] is JsonArray arr)
                        {
                            foreach (var e in arr)
                            {
                                if (e is null || !IsString(e))
                                {
                                    problems.Add(new($"questions.{key}.criteria[{i}].examples", "examples must be strings"));
                                    break;
                                }
                                ex.Add(e.GetValue<string>());
                            }
                        }
                        var exStr = ex.Count > 0 ? " Examples: " + string.Join(", ", ex) : "";
                        descs.Add(PyStr.Strip(what + exStr));
                    }
                    else if (IsString(item))
                    {
                        descs.Add(PyStr.Strip(item.GetValue<string>()));
                    }
                    else
                    {
                        problems.Add(new($"questions.{key}.criteria[{i}]", "von-1.2.0 accepts text or {what, examples} score levels"));
                        descs.Add("");
                    }
                }
                return new VonQuestion(key, "score", descs, descs, ins, false);
            }
            case NoulQuestion n:
            {
                string? pos = null, neg = null;
                foreach (var (k, v) in n.Criteria ?? new Dictionary<string, JsonNode?>())
                {
                    if (v is null || !IsString(v))
                    {
                        problems.Add(new($"questions.{key}.criteria.{k}", "von-1.2.0 accepts only text true/false descriptions"));
                        continue;
                    }
                    if (k == "true") pos = v.GetValue<string>();
                    else if (k == "false") neg = v.GetValue<string>();
                }
                var explicitCriteria = PyStr.Truthy(pos) || PyStr.Truthy(neg);
                var descs = new[]
                {
                    PyStr.Truthy(pos) ? pos! : "Yes, condition holds true.",
                    PyStr.Truthy(neg) ? neg! : "No, condition is false.",
                };
                return new VonQuestion(key, "noul", [], descs, ins, explicitCriteria);
            }
            default:
                throw new ArgumentException($"unsupported question type {q.GetType().Name}", nameof(q));
        }
    }

    /// <summary>The formatted state text (<c>_format_state</c>), used for packing and for the temperature map.</summary>
    /// <param name="state">The request state.</param>
    public static string FormatState(JsonNode? state) => PyRepr.FormatVonState(state);

    /// <summary>Token count of the formatted state without special tokens (the temperature map's feature).</summary>
    /// <param name="stateText">Formatted state.</param>
    public int StateTokens(string stateText) => _tok.Encode(stateText).Length;

    /// <summary>Builds the rows for a request, in the order the reference evaluates them.</summary>
    /// <param name="stateText">Formatted state.</param>
    /// <param name="questions">Normalised questions.</param>
    public IReadOnlyList<VonRow> Build(string stateText, IReadOnlyList<VonQuestion> questions)
    {
        var rows = new List<VonRow>();
        var problems = new List<ValidationProblem>();
        foreach (var q in questions)
        {
            var kind = q.Type switch { "choice" => VonRowKind.Choice, "score" => VonRowKind.Score, _ => VonRowKind.Noul };
            rows.Add(Row(q, kind, stateText, problems));
            if (kind == VonRowKind.Noul && !q.ExplicitNoulCriteria)
                rows.Add(Row(q, VonRowKind.NoulNull, "", problems));
        }
        if (problems.Count > 0) throw new DecisionRejectedException(problems);
        return rows;
    }

    private VonRow Row(VonQuestion q, VonRowKind kind, string stateText, List<ValidationProblem> problems)
    {
        var packed = Pack(stateText, q.Instructions, q.Descriptions);
        var inner = _tok.Encode(packed);
        var ids = new int[inner.Length + 2];
        ids[0] = _tok.ClsId;
        inner.CopyTo(ids, 1);
        ids[^1] = _tok.SepId;
        if (ids.Length > _maxTokens)
            problems.Add(new($"questions.{q.Key}", $"packed sequence is {ids.Length} tokens; this server caps von-1.2.0 at {_maxTokens}"));
        var markers = new List<int>();
        for (var i = 0; i < ids.Length; i++) if (ids[i] == _tok.MaskId) markers.Add(i);
        return new VonRow(q, kind, ids, markers.ToArray());
    }

    /// <summary><c>OptionMarkerModel.pack_sequence</c>.</summary>
    private string Pack(string state, string question, IReadOnlyList<string> options)
    {
        var prefix = PyStr.Truthy(question) ? PyStr.Strip($"{question} {state}") : PyStr.Strip(state);
        var opts = string.Join(" ", options.Select(o => $"{_tok.MaskToken} {PyStr.Strip(o)}"));
        var packed = $"{prefix} {_tok.SepToken} {opts}";
        return _post.DigitSplit ? DigitRun.Replace(packed, m => string.Join(" ", m.Value.ToCharArray())) : packed;
    }

    /// <summary>
    /// Pads rows into one batch and builds von-sdk's order-invariant position ids and additive attention masks
    /// (<c>build_option_invariant_position_ids</c>, <c>build_independent_option_masks</c>): an option token sees the
    /// prefix and its own option only; each option's positions restart at the prefix length; the sliding mask is
    /// computed from those position ids. K is padded to at least <see cref="VonLimits.MinK"/> with masked slots.
    /// </summary>
    /// <param name="rows">Rows.</param>
    /// <param name="padId">Pad token id.</param>
    public VonBatch Collate(IReadOnlyList<VonRow> rows, int padId)
    {
        int b = rows.Count, s = rows.Max(r => r.InputIds.Length), k = Math.Max(_limits.MinK, rows.Max(r => r.Markers.Length));
        var ids = new long[b * s];
        var pos = new long[b * s];
        var full = new float[b * s * s];
        var slide = new float[b * s * s];
        var mpos = new long[b * k];
        var mmask = new bool[b * k];
        var window = _limits.SlidingWindow;
        var optionId = new int[s];
        var blocked = float.MinValue;

        for (var r = 0; r < b; r++)
        {
            var row = rows[r];
            var len = row.InputIds.Length;
            for (var i = 0; i < s; i++)
            {
                ids[r * s + i] = i < len ? row.InputIds[i] : padId;
                pos[r * s + i] = i;
                optionId[i] = -1;
            }
            var lastContent = len - 1;  // the trailing [SEP] belongs to no option
            var markers = row.Markers;
            for (var m = 0; m < markers.Length; m++)
            {
                var start = markers[m];
                var end = m + 1 < markers.Length ? markers[m + 1] : lastContent;
                for (var i = start; i < end; i++)
                {
                    optionId[i] = m;
                    pos[r * s + i] = markers[0] + (i - start);
                }
                mpos[r * k + m] = start;
                mmask[r * k + m] = true;
            }

            var baseOff = r * s * s;
            for (var qi = 0; qi < s; qi++)
            {
                var qPrefix = optionId[qi] == -1;
                var pq = pos[r * s + qi];
                for (var kj = 0; kj < s; kj++)
                {
                    bool allowed;
                    if (qi == kj) allowed = true;  // eye: a fully masked row would produce NaN
                    else
                    {
                        var kPrefix = optionId[kj] == -1;
                        allowed = (qPrefix ? kPrefix : kPrefix || optionId[kj] == optionId[qi]) && kj < len;
                    }
                    full[baseOff + qi * s + kj] = allowed ? 0f : blocked;
                    var local = Math.Abs(pq - pos[r * s + kj]) <= window;
                    slide[baseOff + qi * s + kj] = (allowed && local) || qi == kj ? 0f : blocked;
                }
            }
        }
        return new VonBatch(b, s, k, ids, full, slide, pos, mpos, mmask);
    }

    /// <summary>von-sdk's <c>_stringify_instructions</c>: dict/list → <c>json.dumps(v, sort_keys=isinstance(v, dict))</c>.</summary>
    private static string StringifyInstructions(JsonNode instructions) =>
        IsString(instructions) ? instructions.GetValue<string>() : PyJson.DumpsAscii(instructions, sortKeys: instructions is JsonObject);

    private static bool IsString(JsonNode n) => n is JsonValue v && v.GetValueKind() == JsonValueKind.String;
}

/// <summary>A padded Von batch, flattened row-major, ready for ONNX Runtime.</summary>
/// <param name="B">Rows.</param>
/// <param name="S">Padded sequence length.</param>
/// <param name="K">Padded option slots.</param>
/// <param name="InputIds">[B,S].</param>
/// <param name="FullMask">[B,1,S,S] additive.</param>
/// <param name="SlidingMask">[B,1,S,S] additive.</param>
/// <param name="PositionIds">[B,S].</param>
/// <param name="MarkerPos">[B,K].</param>
/// <param name="MarkerMask">[B,K].</param>
public sealed record VonBatch(int B, int S, int K, long[] InputIds, float[] FullMask, float[] SlidingMask,
    long[] PositionIds, long[] MarkerPos, bool[] MarkerMask);
