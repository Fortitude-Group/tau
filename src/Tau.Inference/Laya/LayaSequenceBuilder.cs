using System.Globalization;
using System.Text.Json.Nodes;
using Tau.Contract;
using Tau.Inference.Engine;
using Tau.Inference.Models;
using Tau.Inference.Text;
using Tau.Inference.Tokenization;

namespace Tau.Inference.Laya;

/// <summary>One Laya question, normalised as <c>laya.Agent._to_internal</c> does, plus its rendered options.</summary>
/// <param name="Key">The question's name in the request.</param>
/// <param name="Type">choice, score or noul.</param>
/// <param name="Instructions">Instructions as text (structured instructions go through <c>json.dumps(ensure_ascii=False)</c>).</param>
/// <param name="Labels">Choice keys, or score legend texts (index order); empty for noul.</param>
/// <param name="Options">Option texts in label-index order, as <c>render_options</c> renders them.</param>
public sealed record LayaQuestion(string Key, string Type, string Instructions, IReadOnlyList<string> Labels, IReadOnlyList<string> Options)
{
    /// <summary>Question type index the model's <c>type_emb</c> uses: choice 0, score 1, noul 2.</summary>
    public long QType => Type switch { "choice" => 0, "score" => 1, _ => 2 };
}

/// <summary>One model row: token ids and option marker positions.</summary>
/// <param name="Question">The question this row answers.</param>
/// <param name="InputIds">Token ids.</param>
/// <param name="Markers">Positions of each option's [MASK] marker.</param>
/// <param name="Truncated">Whether the state was cut to fit.</param>
public sealed record LayaRow(LayaQuestion Question, int[] InputIds, int[] Markers, bool Truncated);

/// <summary>
/// Builds Laya's input rows exactly as laya 0.3.20 does (<c>render_options</c>, <c>build_sequence</c>,
/// <c>Agent._encode_state</c>): <c>[CLS] &lt;type&gt; question: instructions [SEP] [MASK] opt0 [MASK] opt1 … [SEP] state [SEP]</c>,
/// one row per question, the state tokenised once and truncated per row (from the left for a list state).
/// </summary>
public sealed class LayaSequenceBuilder
{
    private const int OptionTokenCap = 48;
    private readonly HfTokenizer _tok;
    private readonly LayaLimits _limits;

    /// <summary>Creates a builder.</summary>
    /// <param name="tokenizer">The package's tokeniser.</param>
    /// <param name="limits">The package's token budgets.</param>
    public LayaSequenceBuilder(HfTokenizer tokenizer, LayaLimits limits)
    {
        _tok = tokenizer;
        _limits = limits;
    }

    /// <summary><c>Agent._to_internal</c> + <c>render_options</c> for a contract question.</summary>
    /// <param name="key">The question's name.</param>
    /// <param name="q">The contract question.</param>
    public static LayaQuestion Normalise(string key, Question q)
    {
        var ins = q.Instructions is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.String
            ? v.GetValue<string>()
            : PyJson.Dumps(q.Instructions);
        switch (q)
        {
            case ChoiceQuestion c:
            {
                var keys = c.Criteria.Select(kv => kv.Key).ToArray();
                // Only None / "" mean "no description" (render_options).
                var opts = c.Criteria.Select(kv => IsNoneOrEmpty(kv.Value) ? kv.Key : $"{kv.Key}: {RenderCriterion(kv.Value)}").ToArray();
                return new LayaQuestion(key, "choice", ins, keys, opts);
            }
            case ScoreQuestion s:
            {
                var rendered = s.Criteria.Select(RenderCriterion).ToArray();
                var opts = rendered.Select((r, i) => string.Create(CultureInfo.InvariantCulture, $"level {i}: {r}")).ToArray();
                return new LayaQuestion(key, "score", ins, rendered, opts);
            }
            case NoulQuestion n:
            {
                JsonNode? f = null, t = null;
                n.Criteria?.TryGetValue("false", out f);
                n.Criteria?.TryGetValue("true", out t);
                var opts = new[]
                {
                    "false: " + (IsNoneOrEmpty(f) ? "no, the statement does not hold" : RenderCriterion(f)),
                    "true: " + (IsNoneOrEmpty(t) ? "yes, the statement holds" : RenderCriterion(t)),
                };
                return new LayaQuestion(key, "noul", ins, [], opts);
            }
            default:
                throw new ArgumentException($"unsupported question type {q.GetType().Name}", nameof(q));
        }
    }

    /// <summary>
    /// Builds one row per question. Rejects (422) a question whose options can't all fit the head budget,
    /// exactly where the reference raises.
    /// </summary>
    /// <param name="state">The request state.</param>
    /// <param name="questions">Normalised questions, in request order.</param>
    public IReadOnlyList<LayaRow> Build(JsonNode? state, IReadOnlyList<LayaQuestion> questions)
    {
        var mask = _tok.MaskToken;
        var truncateLeft = state is JsonArray;
        var stateIds = _tok.Encode(PyJson.DumpsState(state).Replace(mask, " ", StringComparison.Ordinal));
        var rows = new List<LayaRow>(questions.Count);
        var problems = new List<ValidationProblem>();
        foreach (var q in questions)
        {
            var row = BuildRow(q, stateIds, truncateLeft);
            if (row.Markers.Length != q.Options.Count)
                problems.Add(new ValidationProblem($"questions.{q.Key}.criteria",
                    $"{q.Options.Count} options don't fit this model's head budget ({_limits.HeadMaxLen} tokens, max_len {_limits.MaxLen}); use fewer or shorter options"));
            rows.Add(row);
        }
        if (problems.Count > 0) throw new DecisionRejectedException(problems);
        return rows;
    }

    private LayaRow BuildRow(LayaQuestion q, int[] stateIds, bool truncateLeft)
    {
        var mask = _tok.MaskToken;
        var headIds = _tok.Encode($"{q.Type} question: {q.Instructions.Replace(mask, " ", StringComparison.Ordinal)}");
        var optIds = new List<int[]>(q.Options.Count);
        foreach (var opt in q.Options)
        {
            var t = _tok.EncodeTruncated(" " + opt.Replace(mask, " ", StringComparison.Ordinal), OptionTokenCap);
            var o = new int[t.Length + 1];
            o[0] = _tok.MaskId;
            t.CopyTo(o, 1);
            optIds.Add(o);
        }

        var headMax = _limits.HeadMaxLen;
        var optBudget = headMax - optIds.Sum(o => o.Length);
        if (optBudget < 16)
        {
            var per = Math.Max(4, FloorDiv(headMax - 16, Math.Max(1, optIds.Count)));
            for (var i = 0; i < optIds.Count; i++) optIds[i] = optIds[i][..Math.Min(per, optIds[i].Length)];
            optBudget = headMax - optIds.Sum(o => o.Length);
        }
        var headLen = Math.Min(headIds.Length, Math.Max(8, optBudget));

        var ids = new List<int>(_limits.MaxLen + 8) { _tok.ClsId };
        ids.AddRange(headIds.AsSpan(0, headLen).ToArray());
        ids.Add(_tok.SepId);
        var markers = new List<int>(optIds.Count);
        foreach (var o in optIds)
        {
            markers.Add(ids.Count);
            ids.AddRange(o);
        }
        ids.Add(_tok.SepId);

        var room = Math.Max(0, _limits.MaxLen - ids.Count - 1);
        // Not state_ids[-room:]: with room 0 that would be the whole state (the reference's own comment).
        var st = truncateLeft
            ? stateIds[Math.Max(0, stateIds.Length - room)..]
            : stateIds[..Math.Min(room, stateIds.Length)];
        ids.AddRange(st);
        ids.Add(_tok.SepId);

        var truncated = st.Length < stateIds.Length || ids.Count > _limits.MaxLen;
        var final = ids.Count > _limits.MaxLen ? ids.GetRange(0, _limits.MaxLen).ToArray() : ids.ToArray();
        return new LayaRow(q, final, markers.Where(m => m < _limits.MaxLen).ToArray(), truncated);
    }

    /// <summary>Laya's <c>render_criterion</c>: strings pass through; anything structured becomes compact JSON.</summary>
    private static string RenderCriterion(JsonNode? value) => PyJson.DumpsState(value);

    private static bool IsNoneOrEmpty(JsonNode? v) =>
        v is null || (v is JsonValue jv && jv.GetValueKind() == System.Text.Json.JsonValueKind.String && jv.GetValue<string>().Length == 0);

    // Python's // floors toward negative infinity.
    private static int FloorDiv(int a, int b) => (int)Math.Floor((double)a / b);
}
