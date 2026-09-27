using Tau.Calibration;
using Tau.Workbench.Measure;
using Tau.Workbench.Spec;

namespace Tau.Workbench.Tests;

/// <summary>Synthetic, seeded model outputs with a known miscalibration.</summary>
internal static class Synthetic
{
    /// <summary>
    /// Draws n items: true logits z, gold sampled from softmax(z), reported probabilities softmax(z × overconfidence).
    /// Temperature scaling on log(reported) should recover T = overconfidence.
    /// </summary>
    public static (double[][] Probs, int[] Gold) Overconfident(int n, int classes, double overconfidence, int seed)
    {
        var rng = new Random(seed);
        var probs = new double[n][];
        var gold = new int[n];
        for (int i = 0; i < n; i++)
        {
            var z = Enumerable.Range(0, classes).Select(_ => Normal(rng) * 1.5).ToArray();
            var q = Softmax.Compute(z);
            double u = rng.NextDouble(), acc = 0;
            gold[i] = classes - 1;
            for (int k = 0; k < classes; k++)
            {
                acc += q[k];
                if (u <= acc)
                {
                    gold[i] = k;
                    break;
                }
            }

            probs[i] = Softmax.Compute(z.Select(x => x * overconfidence).ToArray());
        }

        return (probs, gold);
    }

    /// <summary>Writes raw run files (records and summary) for a model, aligned with the repo's item ids and gold labels.</summary>
    public static void WriteRawRuns(TestRepo repo, string model, double overconfidence, int seed, string? modelHash = StubSystemOne.Hash, bool tau = true)
    {
        var spec = repo.Spec;
        foreach (var (split, prefix) in new[] { ("calibration", "c"), ("heldout", "h") })
        {
            var items = Data.PreparedDataset.LoadSplit(spec, repo.Manifest, split);
            var records = new List<MeasuredItem>();
            var rng = new Random(seed + split.Length);
            for (int i = 0; i < items.Count; i++)
            {
                var gold = spec.Question.ClassIndex(items[i].Label)!.Value;
                var p = Draw(rng, spec.Question.Classes.Count, gold, overconfidence);
                records.Add(new MeasuredItem { ItemId = items[i].Id, Gold = items[i].Label, LatencyMs = 10 + (i % 7) }.WithVector(spec.Question, p));
            }

            WriteRun(spec, model, split, Phases.Raw, records, modelHash, tau);
        }
    }

    public static void WriteRun(DecisionSpec spec, string model, string split, string phase, IReadOnlyList<MeasuredItem> records, string? modelHash, bool tau = true)
    {
        var identity = tau
            ? new EndpointIdentity(true, "Tau Runtime at stub", [new EndpointModel(model, "laya", "rev", modelHash, true)], [])
            : new EndpointIdentity(false, EndpointIdentity.NotTau, [], []);
        var summary = MeasureStage.Summarise(spec, model, split, phase, records) with
        {
            Endpoint = "http://localhost:18093/",
            EndpointIdentity = identity,
            ModelHash = tau ? modelHash : null,
            DurationSeconds = records.Count * 0.01,
            Concurrency = 4,
            Gpu = new GpuPower(200, 10, 500, "Test GPU", GpuPowerSampler.WholeGpuNote),
            StartedUtc = "2026-09-27T10:00:00Z",
        };
        WorkbenchJson.WriteJsonl(spec.RunPath(model, split, phase), records);
        WorkbenchJson.WriteJson(spec.RunSummaryPath(model, split, phase), summary);
    }

    /// <summary>A vector whose argmax is gold ~70% of the time, sharpened by <paramref name="overconfidence"/>.</summary>
    public static double[] Draw(Random rng, int classes, int gold, double overconfidence)
    {
        var z = Enumerable.Range(0, classes).Select(_ => Normal(rng)).ToArray();
        z[gold] += 1.2;
        return Softmax.Compute(z.Select(x => x * overconfidence).ToArray());
    }

    private static double Normal(Random rng) => Math.Sqrt(-2 * Math.Log(1 - rng.NextDouble())) * Math.Cos(2 * Math.PI * rng.NextDouble());
}
