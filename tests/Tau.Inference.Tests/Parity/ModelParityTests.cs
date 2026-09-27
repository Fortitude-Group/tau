using System.Text.Json.Nodes;
using Tau.Contract;
using Tau.Inference.Engine;
using Tau.Inference.Laya;
using Tau.Inference.PostProcessing;
using Tau.Inference.Von;

namespace Tau.Inference.Tests.Parity;

/// <summary>
/// The C# parity gates (T030/T031/T034) for every exported model, against the reference fixtures:
/// sequences (token ids, markers, Von position ids) exact; logits within tolerance with the same argmax; full
/// contract answers within tolerance with the same choice. Contract-valid requests the reference rejects must be
/// rejected (422) here too.
/// </summary>
[Trait("Category", "Parity")]
[Trait("Category", "Models")]
public sealed class ModelParityTests
{
    public static TheoryData<string> Models => new(ParityFixture.ModelIds);

    [Theory]
    [MemberData(nameof(Models))]
    public void Sequences_match_the_reference_exactly(string model)
    {
        var (header, seqs) = ParityFixture.Read(model, "sequences.jsonl");
        var (_, answers) = ParityFixture.Read(model, "answers.jsonl");
        ParityFixture.AssertSameExport(header, model);
        var m = ParityFixture.Engine.Model(model);
        var failures = new List<string>();

        foreach (var (id, record) in answers)
        {
            var request = ParityFixture.Request(record, model);
            if (record["error"] is not null)
            {
                // The reference raised: Tau must refuse the same request rather than answer it.
                var threw = false;
                try { BuildRows(m, request); } catch (DecisionRejectedException) { threw = true; }
                if (!threw) failures.Add($"{id}: reference rejected it ({record["error"]}), Tau built rows");
                continue;
            }

            var expected = seqs[id]["rows"]!.AsArray();
            var actual = BuildRows(m, request);
            if (actual.Count != expected.Count)
            {
                failures.Add($"{id}: {actual.Count} rows, reference {expected.Count}");
                continue;
            }
            for (var r = 0; r < actual.Count; r++)
            {
                var e = expected[r]!.AsObject();
                var (ids, markers, pos) = actual[r];
                var eIds = e["input_ids"]!.AsArray().Select(x => x!.GetValue<int>()).ToArray();
                var eMarkers = e["markers"]!.AsArray().Select(x => x!.GetValue<int>()).ToArray();
                if (!ids.SequenceEqual(eIds)) failures.Add($"{id}/row{r}: token ids differ at {FirstDiff(ids, eIds)} (len {ids.Length} vs {eIds.Length})");
                if (!markers.SequenceEqual(eMarkers)) failures.Add($"{id}/row{r}: markers [{string.Join(",", markers)}] vs [{string.Join(",", eMarkers)}]");
                if (e["position_ids"] is JsonArray ePos && pos is not null)
                {
                    var ep = ePos.Select(x => x!.GetValue<long>()).ToArray();
                    if (!pos.SequenceEqual(ep)) failures.Add($"{id}/row{r}: position ids differ at {FirstDiff(pos, ep)}");
                }
            }
        }
        Assert.True(failures.Count == 0, $"{model}: {failures.Count} sequence mismatches\n" + string.Join("\n", failures.Take(15)));
    }

    [Theory]
    [MemberData(nameof(Models))]
    public void Logits_match_the_reference_within_tolerance(string model)
    {
        var (header, logits) = ParityFixture.Read(model, "logits.jsonl");
        var (_, answers) = ParityFixture.Read(model, "answers.jsonl");
        ParityFixture.AssertSameExport(header, model);
        var m = ParityFixture.Engine.Model(model);
        double maxDl = 0, maxDp = 0;
        var failures = new List<string>();

        foreach (var (id, record) in answers)
        {
            if (record["error"] is not null) continue;
            var request = ParityFixture.Request(record, model);
            var actual = RunRows(m, request);
            var expected = logits[id]["logits"]!.AsArray().Select(r => r!.AsArray().Select(x => x!.GetValue<float>()).ToArray()).ToArray();
            Assert.Equal(expected.Length, actual.Length);
            for (var r = 0; r < actual.Length; r++)
            {
                var (e, a) = (expected[r], actual[r]);
                Assert.Equal(e.Length, a.Length);
                var dl = e.Zip(a, (x, y) => Math.Abs((double)x - y)).Max();
                var pe = Numerics.SoftmaxF32(e);
                var pa = Numerics.SoftmaxF32(a);
                var dp = pe.Zip(pa, (x, y) => Math.Abs((double)x - y)).Max();
                maxDl = Math.Max(maxDl, dl);
                maxDp = Math.Max(maxDp, dp);
                if (dl > ParityFixture.TolLogit || dp > ParityFixture.TolProb) failures.Add($"{id}/row{r}: dlogit {dl:E2} dprob {dp:E2}");
                if (Numerics.ArgMaxTie(e) != Numerics.ArgMaxTie(a)) failures.Add($"{id}/row{r}: argmax {Numerics.ArgMaxTie(e)} vs {Numerics.ArgMaxTie(a)}");
            }
        }
        TestContext.Current.SendDiagnosticMessage($"{model}: max |dlogit| {maxDl:E2}, max |dprob| {maxDp:E2}");
        Assert.True(failures.Count == 0, $"{model}: {failures.Count} logit mismatches (max dlogit {maxDl:E2}, dprob {maxDp:E2})\n" + string.Join("\n", failures.Take(15)));
    }

    [Theory]
    [MemberData(nameof(Models))]
    public async Task Answers_match_the_reference_end_to_end(string model)
    {
        var (header, answers) = ParityFixture.Read(model, "answers.jsonl");
        ParityFixture.AssertSameExport(header, model);
        var failures = new List<string>();

        foreach (var (id, record) in answers)
        {
            var request = ParityFixture.Request(record, model);
            if (record["error"] is not null)
            {
                await Assert.ThrowsAsync<DecisionRejectedException>(() =>
                    ParityFixture.Engine.DecideAsync(request, new DecisionOptions(Raw: true), TestContext.Current.CancellationToken));
                continue;
            }
            var result = await ParityFixture.Engine.DecideAsync(request, new DecisionOptions(Raw: true), TestContext.Current.CancellationToken);
            Assert.Equal(model, result.Response.Model);
            foreach (var (qid, expectedNode) in record["expected"]!.AsObject())
                Compare($"{id}/{qid}", expectedNode!.AsObject(), result.Response.Answers[qid], failures);
        }
        Assert.True(failures.Count == 0, $"{model}: {failures.Count} answer mismatches\n" + string.Join("\n", failures.Take(20)));
    }

    private static void Compare(string where, JsonObject e, Answer a, List<string> failures)
    {
        void Close(string field, double expected, double actual, double tol)
        {
            if (Math.Abs(expected - actual) > tol) failures.Add($"{where}.{field}: {actual} vs reference {expected}");
        }

        switch (a)
        {
            case ChoiceAnswer c:
                var refChoice = e["choice"]!.GetValue<string>();
                if (c.Choice != refChoice)
                {
                    // Only a tie may differ: options scored identically in exact arithmetic (e.g. duplicate text,
                    // which Von scores independently) are separated by float noise alone, so the reference's pick is
                    // arbitrary. Tau takes the first tied index (DECISIONS "argmax tie-break"). Anything else fails.
                    var ep = e["probabilities"]!.AsObject();
                    var tied = Math.Abs(ep[c.Choice]!.GetValue<double>() - ep[refChoice]!.GetValue<double>()) <= 1e-4;
                    if (!tied) failures.Add($"{where}: choice '{c.Choice}' vs '{refChoice}'");
                }
                foreach (var (k, v) in e["probabilities"]!.AsObject()) Close($"p[{k}]", v!.GetValue<double>(), c.Probabilities[k], ParityFixture.TolProb);
                Close("confidence", e["confidence"]!.GetValue<double>(), c.Confidence, 2e-3);
                break;
            case ScoreAnswer s:
                var k2 = s.Probabilities.Count;
                Close("score", e["score"]!.GetValue<double>(), s.Score, Math.Max(ParityFixture.TolProb * (k2 - 1), 0.011));
                foreach (var (k, v) in e["probabilities"]!.AsObject()) Close($"p[{k}]", v!.GetValue<double>(), s.Probabilities[k], ParityFixture.TolProb);
                Close("confidence", e["confidence"]!.GetValue<double>(), s.Confidence, 2e-3);
                // Structured levels: the reference echoes the raw object into the legend, which the contract's
                // string-valued legend can't carry; Tau renders it (see DECISIONS). Compare text levels only.
                foreach (var (k, v) in e["legend"]!.AsObject())
                    if (v is JsonValue jv && jv.GetValueKind() == System.Text.Json.JsonValueKind.String && s.Legend[k] != jv.GetValue<string>())
                        failures.Add($"{where}.legend[{k}]: '{s.Legend[k]}' vs '{jv}'");
                break;
            case NoulAnswer n:
                Close("noul", e["noul"]!.GetValue<double>(), n.Noul, ParityFixture.TolProb);
                break;
        }
    }

    private static List<(int[] Ids, int[] Markers, long[]? Pos)> BuildRows(OnnxModel m, DecisionRequest request)
    {
        if (m.Package.Family == Tau.Inference.Models.ModelFamily.Laya)
        {
            var qs = request.Questions.Select(kv => LayaSequenceBuilder.Normalise(kv.Key, kv.Value)).ToArray();
            return new LayaSequenceBuilder(m.Tokenizer, m.Package.LayaLimits!).Build(request.State, qs)
                .Select(r => (r.InputIds, r.Markers, (long[]?)null)).ToList();
        }
        var problems = new List<ValidationProblem>();
        var vq = request.Questions.Select(kv => VonSequenceBuilder.Normalise(kv.Key, kv.Value, problems)).ToArray();
        if (problems.Count > 0) throw new DecisionRejectedException(problems);
        var b = new VonSequenceBuilder(m.Tokenizer, m.Package.VonLimits!, m.Package.VonPost!, 4096);
        var rows = b.Build(VonSequenceBuilder.FormatState(request.State), vq!);
        return rows.Select(r =>
        {
            var batch = b.Collate([r], m.Tokenizer.PadId);
            return (r.InputIds, r.Markers, (long[]?)batch.PositionIds);
        }).ToList();
    }

    private static float[][] RunRows(OnnxModel m, DecisionRequest request)
    {
        if (m.Package.Family == Tau.Inference.Models.ModelFamily.Laya)
        {
            var qs = request.Questions.Select(kv => LayaSequenceBuilder.Normalise(kv.Key, kv.Value)).ToArray();
            return m.RunLaya(new LayaSequenceBuilder(m.Tokenizer, m.Package.LayaLimits!).Build(request.State, qs), out _);
        }
        var problems = new List<ValidationProblem>();
        var vq = request.Questions.Select(kv => VonSequenceBuilder.Normalise(kv.Key, kv.Value, problems)).ToArray();
        var b = new VonSequenceBuilder(m.Tokenizer, m.Package.VonLimits!, m.Package.VonPost!, 4096);
        return m.RunVon(b, b.Build(VonSequenceBuilder.FormatState(request.State), vq!), out _, out _);
    }

    private static int FirstDiff<T>(T[] a, T[] b) where T : IEquatable<T>
    {
        var n = Math.Min(a.Length, b.Length);
        for (var i = 0; i < n; i++) if (!a[i].Equals(b[i])) return i;
        return n;
    }
}
