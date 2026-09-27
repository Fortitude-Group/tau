using System.Diagnostics;
using Microsoft.ML.OnnxRuntime;
using Tau.Inference.Laya;
using Tau.Inference.Models;
using Tau.Inference.Onnx;
using Tau.Inference.Tokenization;
using Tau.Inference.Von;

namespace Tau.Inference.Engine;

/// <summary>A loaded model: its package, tokeniser, ONNX session and the forward pass for its family.</summary>
public sealed class OnnxModel : IDisposable
{
    // A Von batch carries two [B,1,S,S] float masks; above this many mask bytes, run the rows in chunks.
    private const long VonMaskBudgetBytes = 512L << 20;

    private OnnxModel(ModelPackage package, HfTokenizer tokenizer, OrtSession session)
    {
        Package = package;
        Tokenizer = tokenizer;
        Session = session;
    }

    /// <summary>The package.</summary>
    public ModelPackage Package { get; }

    /// <summary>The tokeniser.</summary>
    public HfTokenizer Tokenizer { get; }

    /// <summary>The ONNX Runtime session.</summary>
    public OrtSession Session { get; }

    /// <summary>Loads the session for a verified package.</summary>
    /// <param name="package">A package loaded with <see cref="ModelPackage.Load"/>.</param>
    /// <param name="provider">Execution provider.</param>
    /// <param name="settings">Session settings.</param>
    public static OnnxModel Load(ModelPackage package, OrtProvider provider, OrtSessionSettings settings)
    {
        var tok = HfTokenizer.Load(package.TokenizerPath);
        try
        {
            return new OnnxModel(package, tok, OrtSessionFactory.Create(package.OnnxPath, provider, settings));
        }
        catch
        {
            tok.Dispose();
            throw;
        }
    }

    /// <summary>Runs Laya rows in one batch and returns each row's option logits (padding slots removed).</summary>
    /// <param name="rows">Rows from <see cref="LayaSequenceBuilder.Build"/>.</param>
    /// <param name="elapsed">Forward-pass time.</param>
    public float[][] RunLaya(IReadOnlyList<LayaRow> rows, out TimeSpan elapsed)
    {
        int b = rows.Count, s = rows.Max(r => r.InputIds.Length);
        var k = Math.Max(Package.LayaLimits!.MinK, rows.Max(r => r.Markers.Length));
        var ids = new long[b * s];
        var att = new long[b * s];
        var mpos = new long[b * k];
        var mmask = new bool[b * k];
        var qtype = new long[b];
        var pad = Tokenizer.PadId;
        for (var r = 0; r < b; r++)
        {
            var row = rows[r];
            for (var i = 0; i < s; i++)
            {
                var real = i < row.InputIds.Length;
                ids[r * s + i] = real ? row.InputIds[i] : pad;
                att[r * s + i] = real ? 1 : 0;
            }
            for (var m = 0; m < row.Markers.Length; m++)
            {
                mpos[r * k + m] = row.Markers[m];
                mmask[r * k + m] = true;
            }
            qtype[r] = row.Question.QType;
        }

        var inputs = new Dictionary<string, OrtValue>
        {
            ["input_ids"] = OrtValue.CreateTensorValueFromMemory(ids, [b, s]),
            ["attention_mask"] = OrtValue.CreateTensorValueFromMemory(att, [b, s]),
            ["marker_pos"] = OrtValue.CreateTensorValueFromMemory(mpos, [b, k]),
            ["marker_mask"] = OrtValue.CreateTensorValueFromMemory(mmask, [b, k]),
            ["qtype"] = OrtValue.CreateTensorValueFromMemory(qtype, [b]),
        };
        var logits = Run(inputs, b, k, out elapsed);
        return rows.Select((row, r) => logits.AsSpan(r * k, row.Markers.Length).ToArray()).ToArray();
    }

    /// <summary>
    /// Runs Von rows and returns each row's option logits. Rows are independent (each has its own sequence and
    /// masks), so a batch whose masks would exceed the memory budget is split into chunks without changing results.
    /// </summary>
    /// <param name="builder">The package's builder (for collation).</param>
    /// <param name="rows">Rows.</param>
    /// <param name="elapsed">Total forward-pass time.</param>
    /// <param name="passes">Forward passes used (1 unless the batch was chunked).</param>
    public float[][] RunVon(VonSequenceBuilder builder, IReadOnlyList<VonRow> rows, out TimeSpan elapsed, out int passes)
    {
        var result = new float[rows.Count][];
        elapsed = TimeSpan.Zero;
        passes = 0;
        var start = 0;
        while (start < rows.Count)
        {
            // Grow the chunk while its padded masks fit the budget (always at least one row).
            var end = start + 1;
            var maxS = rows[start].InputIds.Length;
            while (end < rows.Count)
            {
                var s2 = Math.Max(maxS, rows[end].InputIds.Length);
                if ((long)(end - start + 1) * s2 * s2 * sizeof(float) * 2 > VonMaskBudgetBytes) break;
                maxS = s2;
                end++;
            }
            var chunk = rows.Skip(start).Take(end - start).ToArray();
            var batch = builder.Collate(chunk, Tokenizer.PadId);
            var inputs = new Dictionary<string, OrtValue>
            {
                ["input_ids"] = OrtValue.CreateTensorValueFromMemory(batch.InputIds, [batch.B, batch.S]),
                ["full_mask"] = OrtValue.CreateTensorValueFromMemory(batch.FullMask, [batch.B, 1, batch.S, batch.S]),
                ["sliding_mask"] = OrtValue.CreateTensorValueFromMemory(batch.SlidingMask, [batch.B, 1, batch.S, batch.S]),
                ["position_ids"] = OrtValue.CreateTensorValueFromMemory(batch.PositionIds, [batch.B, batch.S]),
                ["marker_pos"] = OrtValue.CreateTensorValueFromMemory(batch.MarkerPos, [batch.B, batch.K]),
                ["marker_mask"] = OrtValue.CreateTensorValueFromMemory(batch.MarkerMask, [batch.B, batch.K]),
            };
            var logits = Run(inputs, batch.B, batch.K, out var t);
            elapsed += t;
            passes++;
            for (var r = 0; r < chunk.Length; r++)
                result[start + r] = logits.AsSpan(r * batch.K, chunk[r].Markers.Length).ToArray();
            start = end;
        }
        return result;
    }

    private float[] Run(Dictionary<string, OrtValue> inputs, int b, int k, out TimeSpan elapsed)
    {
        try
        {
            using var runOptions = new RunOptions();
            var sw = Stopwatch.StartNew();
            using var outputs = Session.Session.Run(runOptions, inputs, ["logits"]);
            elapsed = sw.Elapsed;
            var span = outputs[0].GetTensorDataAsSpan<float>();
            if (span.Length != b * k)
                throw new InvalidOperationException($"model returned {span.Length} logits, expected {b}×{k}");
            return span.ToArray();
        }
        finally
        {
            foreach (var v in inputs.Values) v.Dispose();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Session.Dispose();
        Tokenizer.Dispose();
    }
}
