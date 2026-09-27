using System.Globalization;
using System.Text;

namespace Tau.Bench;

/// <summary>Writes one run's <c>latency-*.md</c>. Layout is fixed and ordered so reruns diff cleanly.</summary>
internal static class MarkdownReport
{
    public static string F(double x) => x.ToString("0.00", CultureInfo.InvariantCulture);

    public static string ProviderLabel(string flavour) => flavour switch
    {
        "cuda" => "CUDA",
        "cpu" => "CPU",
        "directml" => "DirectML",
        _ => flavour,
    };

    /// <summary>What the number is, why it matters and what follows from it (constitution XII), per measurement.</summary>
    public static string Explain(string measurementId) => measurementId switch
    {
        "model_forward" =>
            "This is the cost of the model itself, the figure closest to what a model card quotes. It's the floor: nothing in "
            + "Tau's own code can make a request faster than this. If the engine figure below is well above it, the extra time is "
            + "Tau's tokenising, row building and post-processing, and that's the place to optimise.",
        "engine_end_to_end" =>
            "This is what a .NET program that embeds Tau.Inference pays per request, with no network hop. The per-question "
            + "columns divide the batched p50 by the number of questions, which is the figure to set against batched "
            + "per-question numbers published elsewhere, and only on comparable hardware.",
        "http_end_to_end" =>
            "This is what a client on the same machine waits for one request. Set it against the in-process engine figure for "
            + "the same provider to see what HTTP, JSON and ASP.NET add. A client on another machine adds its network round trip on top.",
        "http_model_header" =>
            "This should match the in-process forward pass on the same provider. If it's clearly higher, something else was "
            + "using the GPU or CPU during the HTTP run and the HTTP figures above should be re-measured before anyone quotes them.",
        _ => "",
    };

    public static string Build(BenchReport r)
    {
        var sb = new StringBuilder();
        var kind = r.Kind == "http" ? "HTTP" : "in-process";
        sb.AppendLine($"# Tau latency: {kind}, {ProviderLabel(r.Provider)}{(r.Tag is null ? "" : $" ({r.Tag})")}");
        sb.AppendLine();
        sb.AppendLine("These are measurements from the reference machine described below, at the precision stated. "
                      + "They say nothing about other hardware. The headline tables use repeat 1. Every figure is in milliseconds.");
        sb.AppendLine();
        AppendHeader(sb, r);
        AppendModels(sb, r);
        AppendWorkload(sb, r);
        foreach (var m in r.Measurements) AppendMeasurement(sb, r, m);
        AppendVariation(sb, r);
        AppendDetail(sb, r);
        return sb.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static void AppendHeader(StringBuilder sb, BenchReport r)
    {
        var h = r.Header;
        var hw = h.Hardware;
        sb.AppendLine("## Run");
        sb.AppendLine();
        sb.AppendLine($"- **Measured (UTC)**: {h.DateUtc}");
        sb.AppendLine($"- **Reproduce with**: `{h.InvokedBy ?? h.Command}`");
        sb.AppendLine($"- **Bench command**: `{h.Command}`");
        sb.AppendLine($"- **Git commit**: `{h.GitCommit}`" + (h.GitDirty switch
        {
            true => " (working tree had uncommitted changes)",
            false => " (clean working tree)",
            _ => "",
        }));
        sb.AppendLine($"- **Contract**: `{h.ContractVersion}`");
        sb.AppendLine($"- **GPU**: {hw.Gpu}" + (hw.GpuVram is null ? "" : $", {hw.GpuVram}") + (hw.GpuDriver is null ? "" : $", driver {hw.GpuDriver}"));
        sb.AppendLine($"- **CPU**: {hw.Cpu}, {(hw.PhysicalCores is { } pc ? $"{pc} physical cores, " : "")}{hw.LogicalCores} logical processors");
        sb.AppendLine($"- **RAM**: {hw.Ram}");
        sb.AppendLine($"- **OS**: {hw.Os}");
        sb.AppendLine($"- **Software**: {h.Software.DotNet} ({h.Software.ProcessArchitecture}), ONNX Runtime {h.Software.OrtManaged} managed"
                      + (h.Software.OrtNative is null ? "" : $" / {h.Software.OrtNative} native") + $", Tau {h.Software.TauVersion}");
        sb.AppendLine($"- **Provider**: requested {ProviderLabel(h.ProviderCheck.Requested)}, ran on {ProviderLabel(h.ProviderCheck.Actual)}"
                      + (h.ProviderCheck.Passed ? "" : " (NOT VERIFIED)"));
        foreach (var e in h.ProviderCheck.Evidence) sb.AppendLine($"  - {e}");
        var precisions = r.Models.Select(m => m.Precision).Distinct().ToArray();
        sb.AppendLine($"- **Precision**: {(precisions.Length == 1 ? precisions[0] + " for every model" : string.Join(", ", r.Models.Select(m => $"{m.Id} {m.Precision}")))}");
        sb.AppendLine($"- **Session**: {string.Join(", ", r.Settings.Session)}");
        sb.AppendLine($"- **Sampling**: {r.Settings.Warmup} warm-up iterations per cell, not timed. Then {r.Settings.Iterations} timed iterations "
                      + $"per cell, and the whole timed pass run {r.Settings.Repeats} time(s) back to back. Timer: {r.Settings.Timer}. "
                      + $"Percentiles: {r.Settings.PercentileMethod}.");
        sb.AppendLine($"- **Machine load just before timing (models already loaded)**: {Load(h.LoadBefore)}");
        sb.AppendLine($"- **Machine load just after timing**: {Load(h.LoadAfter)}");
        sb.AppendLine($"  - {h.LoadBefore.Method}. A snapshot, so it shows whether the machine was busy, not what happened during every iteration.");
        foreach (var n in h.Notes) sb.AppendLine($"- **Note**: {n}");
        sb.AppendLine();
    }

    private static string Load(LoadSample s) =>
        $"CPU {(s.CpuPercent is { } c ? F(c) + " %" : "unknown")}, GPU {(s.GpuPercent is { } g ? F(g) + " %" : "unknown")}"
        + (s.GpuMemoryUsedMiB is { } m ? $", GPU memory in use {m:0} MiB" : "");

    private static void AppendModels(StringBuilder sb, BenchReport r)
    {
        sb.AppendLine("## Models");
        sb.AppendLine();
        sb.AppendLine("| Model | Family | Upstream revision | ONNX sha256 | Precision | Reference |");
        sb.AppendLine("|---|---|---|---|---|---|");
        foreach (var m in r.Models)
            sb.AppendLine($"| {m.Id} | {m.Family} | `{m.Revision}` | `{m.OnnxSha256}` | {m.Precision} | {m.ReferencePackage ?? ""} |");
        sb.AppendLine();
    }

    private static void AppendWorkload(StringBuilder sb, BenchReport r)
    {
        var w = r.Workload;
        sb.AppendLine("## Workload");
        sb.AppendLine();
        sb.AppendLine($"`tools/Tau.Bench/workload.json` ({w.Name}, canonical sha256 `{w.Sha256}`). {w.Description} "
                      + $"The state is about {w.StateWords} words. Each request pins `model` to the model id, so no auto-routing is involved.");
        sb.AppendLine();
        sb.AppendLine("| Questions | Types, in order |");
        sb.AppendLine("|---|---|");
        foreach (var q in r.Settings.Questions)
            sb.AppendLine($"| {q} | {string.Join(", ", w.QuestionTypes[q.ToString(CultureInfo.InvariantCulture)])} |");
        sb.AppendLine();

        var first = r.Measurements[0];
        sb.AppendLine("Input size per request (batch rows sent to the model / input tokens processed):");
        sb.AppendLine();
        sb.AppendLine("| Model | " + string.Join(" | ", r.Settings.Questions.Select(q => $"q={q}")) + " |");
        sb.AppendLine("|---|" + string.Concat(r.Settings.Questions.Select(_ => "---|")));
        foreach (var model in r.Models)
            sb.AppendLine($"| {model.Id} | " + string.Join(" | ", r.Settings.Questions.Select(q =>
            {
                var c = first.Cells.First(c => c.Model == model.Id && c.Questions == q);
                return (c.BatchRows < 0 ? "n/a" : c.BatchRows.ToString(CultureInfo.InvariantCulture)) + " / " + c.InputTokens;
            })) + " |");
        sb.AppendLine();
        sb.AppendLine("Both families build one row per question with the whole state in it, so the tokens grow with the question count. "
                      + "Von also adds a state-free row for each noul question (its zero-shot prior correction), so a Von request "
                      + "sends more rows than it has questions. The models use different tokenisers, so their token counts differ for the same text.");
        sb.AppendLine();
    }

    private static void AppendMeasurement(StringBuilder sb, BenchReport r, Measurement m)
    {
        sb.AppendLine($"## {m.Title} (ms)");
        sb.AppendLine();
        sb.AppendLine(m.What);
        sb.AppendLine();
        var qs = r.Settings.Questions;
        var cols = new List<string> { "Model" };
        foreach (var q in qs)
        {
            cols.Add($"q={q} p50 / p95 / p99");
            if (q > 1) cols.Add($"q={q} per question (p50)");
        }
        sb.AppendLine("| " + string.Join(" | ", cols) + " |");
        sb.AppendLine("|---|" + string.Concat(cols.Skip(1).Select(_ => "---:|")));
        foreach (var model in r.Models)
        {
            var cells = new List<string> { model.Id };
            foreach (var q in qs)
            {
                var s = m.Cells.First(c => c.Model == model.Id && c.Questions == q).Repeats[0];
                cells.Add($"{F(s.P50)} / {F(s.P95)} / {F(s.P99)}");
                if (q > 1) cells.Add(F(s.P50PerQuestion));
            }
            sb.AppendLine("| " + string.Join(" | ", cells) + " |");
        }
        sb.AppendLine();
        sb.AppendLine(Explain(m.Id));
        sb.AppendLine();
    }

    private static void AppendVariation(StringBuilder sb, BenchReport r)
    {
        sb.AppendLine("## Run-to-run variation");
        sb.AppendLine();
        if (r.Settings.Repeats < 2)
        {
            sb.AppendLine("Only one repeat was run, so there's no variation to report and SC-007 can't be judged from this file.");
            sb.AppendLine();
            return;
        }
        sb.AppendLine("| Measurement | Model | q | p50 repeat 1 | p50 repeat 2 | Change |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|");
        foreach (var m in r.Measurements)
        foreach (var c in m.Cells.Where(c => c.Repeats.Count >= 2))
            sb.AppendLine($"| {m.Title} | {c.Model} | {c.Questions} | {F(c.Repeats[0].P50)} | {F(c.Repeats[1].P50)} | {Signed(c.P50RepeatDiffPercent)} |");
        sb.AppendLine();
        var worst = r.Measurements.SelectMany(m => m.Cells).Where(c => c.Repeats.Count >= 2).Max(c => Math.Abs(c.P50RepeatDiffPercent ?? 0));
        sb.AppendLine($"The two timed passes ran back to back on the same process and models. The largest p50 change between them is "
                      + $"{F(worst)} %. SC-007 asks that a rerun of the command reproduces the headline figures within the variation "
                      + "a report records, and this is that variation. A change of more than a few percent means the machine wasn't "
                      + "quiet, and the figures should be re-measured before they're quoted.");
        sb.AppendLine();
    }

    public static string Signed(double? pct) => pct is { } p ? (p >= 0 ? "+" : "") + F(p) + " %" : "n/a";

    private static void AppendDetail(StringBuilder sb, BenchReport r)
    {
        sb.AppendLine("## Full distributions (repeat 1)");
        sb.AppendLine();
        sb.AppendLine("Standard deviation is the sample standard deviation. Later repeats are in the JSON file next to this one.");
        sb.AppendLine();
        foreach (var m in r.Measurements)
        {
            sb.AppendLine($"### {m.Title}");
            sb.AppendLine();
            sb.AppendLine("| Model | q | n | mean | sd | min | p50 | p95 | p99 | max |");
            sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            foreach (var c in m.Cells.Where(c => c.Repeats.Count >= 2))
            {
                var s = c.Repeats[0];
                sb.AppendLine($"| {c.Model} | {c.Questions} | {s.N} | {F(s.Mean)} | {F(s.Sd)} | {F(s.Min)} | {F(s.P50)} | {F(s.P95)} | {F(s.P99)} | {F(s.Max)} |");
            }
            sb.AppendLine();
        }
    }
}
