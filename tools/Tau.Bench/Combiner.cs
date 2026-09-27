using System.Text;
using static Tau.Bench.MarkdownReport;

namespace Tau.Bench;

/// <summary>Merges the per-run <c>latency-*.json</c> files in a directory into <c>latency.md</c> and <c>latency.json</c>.</summary>
internal static class Combiner
{
    private static readonly string[] ProviderOrder = ["cuda", "directml", "cpu"];

    public static (string Md, string Json) Combine(string dir)
    {
        var files = Directory.GetFiles(dir, "latency-*.json")
            .Where(f => !Path.GetFileName(f).Equals("latency.json", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (files.Length == 0) throw new BenchRefusedException($"no latency-*.json files in {dir}");

        var runs = files.Select(f => (File: Path.GetFileName(f), Report: ReportJson.Read<BenchReport>(f)))
            .OrderBy(x => x.Report.Kind == "http" ? 1 : 0)
            .ThenBy(x => Array.IndexOf(ProviderOrder, x.Report.Provider) is var i && i >= 0 ? i : 99)
            .ThenBy(x => x.Report.Tag ?? "", StringComparer.Ordinal)
            .ThenBy(x => x.File, StringComparer.Ordinal)
            .ToArray();

        var combined = new CombinedReport("latency-combined", "Tau.Bench --combine " + Path.GetFileName(dir.TrimEnd('/', '\\')),
            runs.Select(r => r.File).ToArray(), runs.Select(r => r.Report).ToArray());
        var jsonPath = Path.Combine(dir, "latency.json");
        var mdPath = Path.Combine(dir, "latency.md");
        ReportJson.Write(jsonPath, combined);
        File.WriteAllText(mdPath, Build(runs));
        return (mdPath, jsonPath);
    }

    private static string Build((string File, BenchReport Report)[] runs)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Tau latency");
        sb.AppendLine();
        sb.AppendLine("A summary of the per-run reports listed below, all measured on the reference machine at the precision stated. "
                      + "The per-run reports hold the full distributions, the provider checks and the run-to-run variation. "
                      + "Every figure is a p50 in milliseconds from repeat 1 unless a column says otherwise.");
        sb.AppendLine();

        sb.AppendLine("## Runs");
        sb.AppendLine();
        sb.AppendLine("| Report | Kind | Provider | Measured (UTC) | Commit | Timed iterations | Load before (CPU / GPU) | Largest p50 change between repeats |");
        sb.AppendLine("|---|---|---|---|---|---:|---|---:|");
        foreach (var (file, r) in runs)
        {
            var h = r.Header;
            var worst = r.Settings.Repeats < 2
                ? "one repeat only"
                : F(r.Measurements.SelectMany(m => m.Cells).Max(c => Math.Abs(c.P50RepeatDiffPercent ?? 0))) + " %";
            sb.AppendLine($"| [{Path.ChangeExtension(file, ".md")}]({Path.ChangeExtension(file, ".md")}) | {r.Kind} | "
                          + $"{ProviderLabel(r.ActualProvider)}{(h.ProviderCheck.Passed ? "" : " (unverified)")} | {h.DateUtc} | "
                          + $"`{h.GitCommit[..Math.Min(12, h.GitCommit.Length)]}`{(h.GitDirty == true ? " (dirty)" : "")} | "
                          + $"{r.Settings.Iterations} x {r.Settings.Repeats} | "
                          + $"{Pct(h.LoadBefore.CpuPercent)} / {Pct(h.LoadBefore.GpuPercent)} | {worst} |");
        }
        sb.AppendLine();

        var hw = runs[0].Report.Header.Hardware;
        var sw = runs[0].Report.Header.Software;
        sb.AppendLine("## Reference machine");
        sb.AppendLine();
        sb.AppendLine($"- **GPU**: {hw.Gpu}{(hw.GpuVram is null ? "" : $", {hw.GpuVram}")}{(hw.GpuDriver is null ? "" : $", driver {hw.GpuDriver}")}");
        sb.AppendLine($"- **CPU**: {hw.Cpu}, {(hw.PhysicalCores is { } pc ? $"{pc} physical cores, " : "")}{hw.LogicalCores} logical processors");
        sb.AppendLine($"- **RAM**: {hw.Ram}");
        sb.AppendLine($"- **OS**: {hw.Os}");
        sb.AppendLine($"- **Software**: {sw.DotNet}, ONNX Runtime {sw.OrtManaged}, Tau {sw.TauVersion}");
        sb.AppendLine($"- **Contract**: `{runs[0].Report.Header.ContractVersion}`");
        var precisions = runs.SelectMany(r => r.Report.Models.Select(m => m.Precision)).Distinct().ToArray();
        sb.AppendLine($"- **Precision**: {string.Join(", ", precisions)}");
        var differing = runs.Where(r => r.Report.Header.Hardware != hw).Select(r => r.File).ToArray();
        if (differing.Length > 0)
            sb.AppendLine($"- **Warning**: {string.Join(", ", differing)} recorded different hardware from {runs[0].File}. Don't compare those runs directly.");
        var hashes = runs.SelectMany(r => r.Report.Models.Select(m => (m.Id, m.OnnxSha256))).Distinct().GroupBy(x => x.Id).Where(g => g.Count() > 1).ToArray();
        if (hashes.Length > 0)
            sb.AppendLine($"- **Warning**: the runs measured different checkpoints of {string.Join(", ", hashes.Select(g => g.Key))}.");
        sb.AppendLine();

        var inProc = runs.Where(r => r.Report.Kind == "in-process").Select(r => r.Report).ToArray();
        if (inProc.Length > 0)
        {
            AppendProviderTable(sb, inProc, "engine_end_to_end", "Engine end to end by provider (p50 ms)",
                "This is the per-request cost for a .NET program that embeds the engine, on each provider. The ratio column says how "
                + "many times slower the CPU is than the GPU for the same request. The per-question columns are the batched p50 divided "
                + "by the number of questions. Compare providers at the question count you'll send most often, since the gap between them changes with q.");
            AppendProviderTable(sb, inProc, "model_forward", "Model forward pass by provider (p50 ms)",
                "This is the model alone, without Tau's tokenising and post-processing. Where the engine figure above is much larger than "
                + "this, the difference is Tau's own code rather than the model.");
        }

        foreach (var (_, http) in runs.Where(r => r.Report.Kind == "http"))
            AppendHttpTable(sb, http, inProc.FirstOrDefault(r => r.Provider == http.Provider));

        sb.AppendLine("## Published figures for context (third party, not measured here)");
        sb.AppendLine();
        sb.AppendLine("These figures come from other people, on other hardware, with timers we haven't inspected. They aren't in any table "
                      + "above and aren't like-for-like with them.");
        sb.AppendLine();
        sb.AppendLine("- Laya's model card (convaiinnovations/laya on Hugging Face) reports 32.8 ms for a single question and 7.2 ms per "
                      + "question batched, on an NVIDIA T4. A T4 is a smaller, older GPU than the one above.");
        sb.AppendLine("- For the hosted Jev endpoint, third parties report a p50 between 236 and 380 ms per request. That includes a network "
                      + "round trip and the hosted service's own queueing. Tau doesn't call the hosted endpoint, so we haven't measured it.");
        sb.AppendLine();
        return sb.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string Pct(double? p) => p is { } x ? F(x) + " %" : "unknown";

    private static Cell? Find(BenchReport r, string measurement, string model, int q) =>
        r.Measurements.FirstOrDefault(m => m.Id == measurement)?.Cells.FirstOrDefault(c => c.Model == model && c.Questions == q);

    private static void AppendProviderTable(StringBuilder sb, BenchReport[] runs, string measurement, string title, string explain)
    {
        sb.AppendLine($"## {title}");
        sb.AppendLine();
        var labels = runs.Select(r => ProviderLabel(r.Provider) + (r.Tag is null ? "" : $" ({r.Tag})")).ToArray();
        var gpu = Array.FindIndex(runs, r => r.Provider is "cuda" or "directml");
        var cpu = Array.FindIndex(runs, r => r.Provider == "cpu");
        var ratio = gpu >= 0 && cpu >= 0;
        var cols = new List<string> { "Model", "q" };
        cols.AddRange(labels);
        if (ratio) cols.Add($"{labels[cpu]} / {labels[gpu]}");
        cols.AddRange(labels.Select(l => $"{l} per question"));
        sb.AppendLine("| " + string.Join(" | ", cols) + " |");
        sb.AppendLine("|---|---:|" + string.Concat(cols.Skip(2).Select(_ => "---:|")));

        var models = runs.SelectMany(r => r.Models.Select(m => m.Id)).Distinct().Order(StringComparer.Ordinal).ToArray();
        var qs = runs.SelectMany(r => r.Settings.Questions).Distinct().Order().ToArray();
        foreach (var model in models)
        foreach (var q in qs)
        {
            var cells = runs.Select(r => Find(r, measurement, model, q)?.Repeats[0]).ToArray();
            var row = new List<string> { model, q.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            row.AddRange(cells.Select(c => c is null ? "n/a" : F(c.P50)));
            if (ratio)
                row.Add(cells[gpu] is { } g && cells[cpu] is { } c2 && g.P50 > 0 ? F(c2.P50 / g.P50) + "x" : "n/a");
            row.AddRange(cells.Select(c => c is null ? "n/a" : F(c.P50PerQuestion)));
            sb.AppendLine("| " + string.Join(" | ", row) + " |");
        }
        sb.AppendLine();
        sb.AppendLine(explain);
        sb.AppendLine();
    }

    private static void AppendHttpTable(StringBuilder sb, BenchReport http, BenchReport? inProc)
    {
        sb.AppendLine($"## HTTP end to end, {ProviderLabel(http.Provider)}{(http.Tag is null ? "" : $" ({http.Tag})")} (p50 ms)");
        sb.AppendLine();
        sb.AppendLine("| Model | q | HTTP p50 | HTTP p95 | HTTP per question | Engine p50 (in-process) | Added by HTTP |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
        var m = http.Measurements.First(x => x.Id == "http_end_to_end");
        foreach (var c in m.Cells)
        {
            var s = c.Repeats[0];
            var e = inProc is null ? null : Find(inProc, "engine_end_to_end", c.Model, c.Questions)?.Repeats[0];
            sb.AppendLine($"| {c.Model} | {c.Questions} | {F(s.P50)} | {F(s.P95)} | {F(s.P50PerQuestion)} | "
                          + $"{(e is null ? "n/a" : F(e.P50))} | {(e is null ? "n/a" : F(s.P50 - e.P50))} |");
        }
        sb.AppendLine();
        sb.AppendLine("This is what a client on the same machine waits for one request. The last column is the HTTP p50 minus the "
                      + "in-process engine p50 from a separate run, so it's an estimate of what HTTP, JSON and ASP.NET add, not a direct "
                      + "measurement. A negative value means the overhead is smaller than the run-to-run noise between the two runs. "
                      + "A remote client adds its network round trip on top.");
        sb.AppendLine();
    }
}
