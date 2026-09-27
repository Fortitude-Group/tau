using System.Text;
using System.Text.RegularExpressions;

namespace Tau.Workbench.Report;

/// <summary>
/// Renders a <see cref="ReportDocument"/> as one self-contained HTML file: inline CSS and inline SVG only,
/// no scripts, no web fonts, no external requests, readable on a light background and about 1,000px wide
/// for screenshots. Every figure has a caption saying what it is, why it matters and what follows
/// (constitution XII). Text that holds a URL is shown without its scheme, so the file contains no link.
/// </summary>
public static partial class HtmlReport
{
    [GeneratedRegex("https?://", RegexOptions.IgnoreCase)]
    private static partial Regex Scheme();

    /// <summary>Renders the page.</summary>
    /// <param name="doc">The report document.</param>
    public static string Render(ReportDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html>\n<html lang=\"en-GB\">\n<head>\n<meta charset=\"utf-8\">\n");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        sb.Append("<title>").Append(E(doc.Title)).Append(" · Tau report</title>\n<style>\n").Append(Css).Append("</style>\n</head>\n<body>\n<main>\n");
        Header(sb, doc);
        DatasetSection(sb, doc);
        ModelsSection(sb, doc);
        ReliabilitySection(sb, doc);
        CalibratorsSection(sb, doc);
        ThresholdSection(sb, doc);
        CascadeSection(sb, doc);
        FrontierSection(sb, doc);
        DatasetLabelsSection(sb, doc);
        BaselinesSection(sb, doc);
        ConfusionSection(sb, doc);
        MissesSection(sb, doc);
        MetadataSection(sb, doc);
        sb.Append("</main>\n</body>\n</html>\n");
        return sb.ToString();
    }

    /// <summary>HTML-encodes text and strips URL schemes, so no link or request can appear in the page.</summary>
    /// <param name="s">The text.</param>
    internal static string E(string? s) => SvgCharts.Esc(Scheme().Replace(s ?? "", ""));

    /// <summary>True when every rate in the document is agreement with the frontier model rather than accuracy.</summary>
    private static bool VsFrontier(ReportDocument doc) => doc.Reference == Spec.ReferenceKind.Frontier;

    /// <summary>The short column heading for a right-answer rate: "Accuracy", or "Agreement" (with the frontier model).</summary>
    private static string RateHead(ReportDocument doc) => VsFrontier(doc) ? "Agreement" : "Accuracy";

    private static void Header(StringBuilder sb, ReportDocument doc)
    {
        sb.Append("<header class=\"top\">\n<p class=\"kicker\">Tau Workbench report</p>\n<h1>").Append(E(doc.Title)).Append("</h1>\n");
        if (doc.Dataset.Synthetic)
        {
            sb.Append("<p><span class=\"badge-synthetic\">Synthetic data</span> Every item in this dataset was generated, not written by real customers. Treat the numbers as a demonstration of the method, not as evidence about real traffic.</p>\n");
        }

        sb.Append("<p class=\"lede\">").Append(E(doc.Summary)).Append("</p>\n");
        sb.Append("<p class=\"caption\">Generated ").Append(E(doc.Metadata.GeneratedUtc)).Append(" (UTC) on ").Append(E(doc.Metadata.Hardware.Gpu))
            .Append(". Every number below is in report.json beside this file; the metadata block at the end says how to reproduce it.</p>\n</header>\n");
    }

    private static void DatasetSection(StringBuilder sb, ReportDocument doc)
    {
        var d = doc.Dataset;
        sb.Append("<section id=\"dataset\">\n<h2>Dataset</h2>\n<dl class=\"facts\">\n");
        Fact(sb, "Name", d.Name);
        Fact(sb, "Source", d.Source ?? "not recorded in the manifest");
        Fact(sb, "Pinned revision", d.Revision ?? "not recorded in the manifest");
        Fact(sb, "Licence", d.Licence ?? "not recorded in the manifest");
        sb.Append("<dt>Synthetic</dt><dd>").Append(d.Synthetic ? "<strong>Yes: generated data</strong>" : "No").Append("</dd>\n");
        foreach (var split in new[] { "finetune", "calibration", "heldout" })
        {
            Fact(sb, split switch { "finetune" => "Fine-tune split", "calibration" => "Calibration split", _ => "Held-out split" },
                d.SplitSizes.TryGetValue(split, out var n) ? Fmt.Int(n) + " items" : "unknown");
        }

        Fact(sb, "Question", $"{doc.QuestionKey} ({doc.QuestionType}, {doc.Classes.Count} answers): {doc.Instructions}");
        sb.Append("</dl>\n<p class=\"caption\">Calibrators and thresholds are fitted on the calibration split only and every result is judged on the held-out split, which nothing was tuned on. The splits are fixed by the dataset manifest (seed 42), and each file's sha256 is checked before use.</p>\n</section>\n");
    }

    private static void MissesSection(StringBuilder sb, ReportDocument doc)
    {
        sb.Append("<section id=\"misses\">\n<h2>The misses</h2>\n");
        if (doc.Misses.Count == 0)
        {
            sb.Append("<p>Nothing fell short of a goal in this run.</p>\n");
        }
        else
        {
            sb.Append("<ul class=\"misses\">\n");
            foreach (var m in doc.Misses)
            {
                sb.Append("<li>").Append(E(m)).Append("</li>\n");
            }

            sb.Append("</ul>\n");
        }

        sb.Append("<p class=\"caption\">These are the results that don't flatter Tau: label noise, calibration that helped little, baselines that win, and anything excluded. They are published with the wins so the favourable numbers can be weighed against them.</p>\n</section>\n");
    }

    private static void MetadataSection(StringBuilder sb, ReportDocument doc)
    {
        var m = doc.Metadata;
        sb.Append("<section id=\"metadata\">\n<h2>Run metadata</h2>\n<dl class=\"facts mono\">\n");
        Fact(sb, "Date (UTC)", m.GeneratedUtc);
        Fact(sb, "Command", m.Command);
        Fact(sb, "Git commit", $"{m.Git.Commit}{(m.Git.Dirty switch { true => " (uncommitted changes outside this run's outputs)", false => " (clean, outputs excluded)", _ => "" })}");
        Fact(sb, "Workbench", m.ToolVersion);
        Fact(sb, "GPU", m.Hardware.Gpu + (m.Hardware.GpuDriver is null ? "" : $" (driver {m.Hardware.GpuDriver})"));
        Fact(sb, "CPU", $"{m.Hardware.Cpu}, {m.Hardware.LogicalCores} logical processors");
        Fact(sb, "OS and runtime", $"{m.Hardware.Os}; {m.Hardware.Runtime}");
        Fact(sb, "Endpoint", m.Endpoint is null ? "not measured yet" : $"{m.Endpoint} ({m.EndpointIdentity?.Description ?? "not identified"})");
        foreach (var model in m.EndpointIdentity?.Models ?? [])
        {
            Fact(sb, "/v1/models: " + model.Id, $"sha256 {model.OnnxSha256 ?? "n/a"}, revision {model.Revision ?? "n/a"}");
        }

        foreach (var (model, hash) in m.ModelHashes)
        {
            Fact(sb, "Measured model " + model, hash ?? "hash unknown (endpoint is not Tau, or not measured)");
        }

        Fact(sb, "Dataset manifest sha256", m.DatasetManifestSha256);
        Fact(sb, "Dataset revision (cache keys)", m.DatasetRevision);
        Fact(sb, "Reference labels", $"{m.Reference.ToString().ToLowerInvariant()}: {m.ReferenceNote}");
        Fact(sb, "Frontier model", m.FrontierModel);
        Fact(sb, "Prompt versions", string.Join(", ", m.PromptVersions));
        Fact(sb, "Frontier cache", m.FrontierCacheCounts.Count == 0 ? "empty" : string.Join(", ", m.FrontierCacheCounts.Select(kv => $"{kv.Key}: {Fmt.Int(kv.Value)} answers")));
        Fact(sb, "Confidence", m.ConfidenceNote);
        sb.Append("</dl>\n<p class=\"caption\">This block ties every figure above to a machine, a model hash, a dataset revision, a date and a command. Re-running the command on the same checkout and models should reproduce the numbers; anything that differs should be explained by what changed here.</p>\n</section>\n");
    }

    private static void Fact(StringBuilder sb, string term, string value) =>
        sb.Append("<dt>").Append(E(term)).Append("</dt><dd>").Append(E(value)).Append("</dd>\n");

    private const string Css = """
:root { color-scheme: light; --page: #f9f9f7; --surface: #fcfcfb; --ink: #0b0b0b; --ink2: #52514e; --muted: #898781;
  --grid: #e1e0d9; --axis: #c3c2b7; --border: rgba(11,11,11,0.10);
  --s1: #2a78d6; --s2: #eb6834; --s3: #1baf7a; --s4: #eda100; --s5: #e87ba4; --s6: #008300; --s7: #4a3aa7; --s8: #e34948;
  --q1: #cde2fb; --q2: #9ec5f4; --q3: #6da7ec; --q4: #3987e5; --q5: #256abf; --q6: #184f95;
  --flag-bg: #fdecc8; --flag-ink: #4a3000; --flag-edge: #c98500; }
@media (prefers-color-scheme: dark) { :root:where(:not([data-theme="light"])) { color-scheme: dark; --page: #0d0d0d; --surface: #1a1a19;
  --ink: #ffffff; --ink2: #c3c2b7; --grid: #2c2c2a; --axis: #383835; --border: rgba(255,255,255,0.10);
  --s1: #3987e5; --s2: #d95926; --s3: #199e70; --s4: #c98500; --s5: #d55181; --s7: #9085e9; --s8: #e66767;
  --flag-bg: #3a2c0c; --flag-ink: #fde7c2; } }
:root[data-theme="dark"] { color-scheme: dark; --page: #0d0d0d; --surface: #1a1a19; --ink: #ffffff; --ink2: #c3c2b7; --grid: #2c2c2a;
  --axis: #383835; --border: rgba(255,255,255,0.10); --s1: #3987e5; --s2: #d95926; --s3: #199e70; --s4: #c98500; --s5: #d55181;
  --s7: #9085e9; --s8: #e66767; --flag-bg: #3a2c0c; --flag-ink: #fde7c2; }
* { box-sizing: border-box; }
body { margin: 0; background: var(--page); color: var(--ink); font: 15px/1.5 system-ui, -apple-system, "Segoe UI", sans-serif; }
main { max-width: 1000px; margin: 0 auto; padding: 24px 16px 48px; }
header.top { padding: 8px 0 4px; }
.kicker { margin: 0; color: var(--muted); font-size: 13px; letter-spacing: 0.04em; text-transform: uppercase; }
h1 { font-size: 26px; line-height: 1.25; margin: 4px 0 12px; }
h2 { font-size: 19px; margin: 0 0 10px; }
h3 { font-size: 15px; margin: 14px 0 6px; }
.lede { font-size: 16px; max-width: 85ch; }
section { background: var(--surface); border: 1px solid var(--border); border-radius: 8px; padding: 20px 24px; margin: 16px 0; }
.caption { color: var(--ink2); font-size: 14px; max-width: 85ch; margin: 10px 0 0; }
.badge-synthetic { display: inline-block; background: var(--flag-bg); color: var(--flag-ink); border: 1px solid var(--flag-edge);
  font-weight: 700; padding: 1px 10px; border-radius: 4px; margin-right: 6px; }
.scroll { overflow-x: auto; }
table { border-collapse: collapse; width: 100%; font-size: 13.5px; }
th, td { text-align: right; padding: 6px 8px; border-bottom: 1px solid var(--grid); vertical-align: top; font-variant-numeric: tabular-nums; }
th:first-child, td:first-child, td.text, th.text { text-align: left; }
th { color: var(--ink2); font-weight: 600; }
td.note { text-align: left; color: var(--ink2); font-size: 12.5px; }
dl.facts { display: grid; grid-template-columns: minmax(140px, 220px) 1fr; gap: 4px 16px; margin: 0; font-size: 14px; }
dl.facts dt { color: var(--ink2); } dl.facts dd { margin: 0; overflow-wrap: anywhere; }
dl.mono dd { font-family: ui-monospace, "Cascadia Mono", Consolas, monospace; font-size: 12.5px; }
.multiples { display: flex; flex-wrap: wrap; gap: 12px 20px; }
figure { margin: 0; } figcaption { font-size: 13px; font-weight: 600; margin-bottom: 2px; }
svg { max-width: 100%; height: auto; display: block; }
svg text { font-family: system-ui, -apple-system, "Segoe UI", sans-serif; }
.tick { font-size: 11px; fill: var(--muted); } .axis-title, .label { font-size: 12px; fill: var(--ink2); }
.grid { stroke: var(--grid); stroke-width: 1; } .axis { stroke: var(--axis); stroke-width: 1; } .ref { stroke: var(--muted); stroke-width: 1; }
.line { fill: none; stroke-width: 2; stroke-linejoin: round; stroke-linecap: round; } .line.dashed { stroke-dasharray: 6 4; stroke-linecap: butt; }
svg.keysvg { display: inline-block; vertical-align: middle; margin-right: 6px; }
.dot { stroke: var(--surface); stroke-width: 2; } .hit { fill: transparent; }
.s1.line { stroke: var(--s1); } .s1.dot, .k1 { fill: var(--s1); background: var(--s1); }
.s2.line { stroke: var(--s2); } .s2.dot, .k2 { fill: var(--s2); background: var(--s2); }
.s3.line { stroke: var(--s3); } .s3.dot, .k3 { fill: var(--s3); background: var(--s3); }
.s4.line { stroke: var(--s4); } .s4.dot, .k4 { fill: var(--s4); background: var(--s4); }
.s5.line { stroke: var(--s5); } .s5.dot, .k5 { fill: var(--s5); background: var(--s5); }
.s6.line { stroke: var(--s6); } .s6.dot, .k6 { fill: var(--s6); background: var(--s6); }
.s7.line { stroke: var(--s7); } .s7.dot, .k7 { fill: var(--s7); background: var(--s7); }
.s8.line { stroke: var(--s8); } .s8.dot, .k8 { fill: var(--s8); background: var(--s8); }
.q0 { fill: var(--grid); } .q1 { fill: var(--q1); } .q2 { fill: var(--q2); } .q3 { fill: var(--q3); } .q4 { fill: var(--q4); } .q5 { fill: var(--q5); } .q6 { fill: var(--q6); }
.cell { font-size: 12px; fill: #0b0b0b; } .cell-dark { font-size: 12px; fill: #ffffff; }
.legend { display: flex; flex-wrap: wrap; gap: 6px 18px; font-size: 13px; color: var(--ink2); margin: 4px 0 10px; }
.key { display: inline-block; width: 18px; height: 4px; border-radius: 2px; vertical-align: middle; margin-right: 6px; }
.key.ref { background: var(--muted); height: 1px; }
details { margin-top: 10px; } summary { cursor: pointer; color: var(--ink2); font-size: 13px; }
.stats { display: grid; grid-template-columns: repeat(auto-fit, minmax(210px, 1fr)); gap: 12px; margin: 6px 0; }
.stats > div { border: 1px solid var(--border); border-radius: 6px; padding: 10px 12px; }
td:first-child:not(.text) { white-space: nowrap; }
.stats .v { font-size: 22px; font-weight: 600; } .stats .l { font-size: 13px; color: var(--ink2); }
.estimate { font-size: 12px; color: var(--ink2); font-weight: 600; }
ul.misses li { margin: 4px 0; } ul.basis { font-size: 13px; color: var(--ink2); padding-left: 18px; }
@media (max-width: 640px) { section { padding: 14px 14px; } dl.facts { grid-template-columns: 1fr; } }

""";
}
