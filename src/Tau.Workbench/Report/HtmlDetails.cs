using System.Text;
using Tau.Workbench.Frontier;

namespace Tau.Workbench.Report;

/// <content>Frontier labels, baselines and confusion sections.</content>
public static partial class HtmlReport
{
    private static void FrontierSection(StringBuilder sb, ReportDocument doc)
    {
        sb.Append("<section id=\"labels\">\n<h2>Frontier labels: noise and agreement</h2>\n");
        var f = doc.Frontier;
        if (f is null)
        {
            sb.Append("<p>The label stage has not run yet.</p>\n</section>\n");
            return;
        }

        var primary = doc.Metadata.PromptVersions[0];
        var alt = doc.Metadata.PromptVersions[1];
        sb.Append("<div class=\"stats\">\n");
        Stat(sb, f.LabelNoise.Rate is null ? "n/a" : Fmt.Pct(f.LabelNoise.Rate), $"disagree with gold ({Fmt.Int(f.LabelNoise.Count)} of {Fmt.Int(f.LabelNoise.N)} items)");
        Stat(sb, f.PromptAgreement.Rate is null ? "n/a" : Fmt.Pct(f.PromptAgreement.Rate), $"agreement between {primary} and {alt} wordings ({Fmt.Int(f.PromptAgreement.Count)} of {Fmt.Int(f.PromptAgreement.N)})");
        foreach (var (split, counts) in f.Splits.Count > 0
                     ? f.Splits.Select(kv => (kv.Key, kv.Value))
                     : [("heldout", new SplitLabelCounts(f.HeldOutItems, f.EligibleItems, f.Pending, f.Cached))])
        {
            Stat(sb, Fmt.Int(counts.Cached.GetValueOrDefault(primary)),
                $"{primary} answers cached of {Fmt.Int(counts.Eligible)} eligible {(split == "heldout" ? "held-out" : split)} items; {Fmt.Int(counts.Pending.GetValueOrDefault(primary))} pending");
        }

        Stat(sb, Fmt.Int(f.Overridden), $"labels overridden by a person; {Fmt.Int(f.Rejected.Count)} answer line(s) rejected");
        sb.Append("</div>\n<p class=\"caption\">")
            .Append(E($"Label noise is how often {f.FrontierModel} disagrees with the dataset's gold label on the same item; it is measured before any human override. Some of that disagreement is the frontier model's error and some is the gold label's, {(VsFrontier(doc) ? "so it says how far the dataset's labels can be trusted; this report scores against the frontier model instead, so it does not bound the figures above" : "so it bounds how precisely any accuracy here can be read")}. Agreement between two wordings of the prompt shows how much the frontier answers depend on phrasing. Answers came from {string.Join("; ", f.ProvenanceProducedBy.DefaultIfEmpty("no answers yet"))}, dated {string.Join(" to ", f.ProvenanceDates.DefaultIfEmpty("n/a"))}; no paid API call was made."))
            .Append("</p>\n");
        if (f.Rejected.Count > 0)
        {
            sb.Append("<details><summary>Rejected answer lines</summary><div class=\"scroll\"><table><thead><tr><th>File</th><th>Line</th><th class=\"text\">Key</th><th class=\"text\">Answer</th><th class=\"text\">Reason</th></tr></thead><tbody>\n");
            foreach (var r in f.Rejected.Take(50))
            {
                sb.Append("<tr><td>").Append(E(r.File)).Append("</td><td>").Append(r.Line).Append("</td><td class=\"text\">").Append(E(r.Key))
                    .Append("</td><td class=\"text\">").Append(E(r.Answer)).Append("</td><td class=\"note\">").Append(E(r.Reason)).Append("</td></tr>\n");
            }

            sb.Append("</tbody></table></div>").Append(f.Rejected.Count > 50 ? $"<p class=\"caption\">First 50 of {Fmt.Int(f.Rejected.Count)}; all are in frontier/label-summary.json.</p>" : "").Append("</details>\n");
        }

        sb.Append("</section>\n");
    }

    private static void DatasetLabelsSection(StringBuilder sb, ReportDocument doc)
    {
        if (doc.DatasetLabels is not { } view)
        {
            return;
        }

        sb.Append("<section id=\"dataset-labels\">\n<h2>Secondary view: against the dataset's own labels</h2>\n");
        sb.Append("<div class=\"scroll\"><table><thead><tr><th>Model</th><th>Agreement with the dataset's labels, raw</th><th>Agreement with the dataset's labels, calibrated</th></tr></thead><tbody>\n");
        foreach (var r in view.Rows)
        {
            sb.Append("<tr><td>").Append(E(r.Kind switch { "frontier" => $"{r.Model} (frontier)", "baseline" => $"{r.Model} (baseline)", _ => $"{r.Model} (Tau)" }))
                .Append("</td><td>").Append(r.Raw is null ? "n/a" : Rate(r.Raw)).Append("</td><td>")
                .Append(r.Kind == "frontier" ? "not calibrated" : r.Calibrated is null ? "n/a" : Rate(r.Calibrated)).Append("</td></tr>\n");
        }

        sb.Append("</tbody></table></div>\n<p class=\"caption\">").Append(E(view.Caption)).Append("</p>\n</section>\n");
    }

    private static void Stat(StringBuilder sb, string value, string label) =>
        sb.Append("<div><div class=\"v\">").Append(E(value)).Append("</div><div class=\"l\">").Append(E(label)).Append("</div></div>\n");

    private static void BaselinesSection(StringBuilder sb, ReportDocument doc)
    {
        sb.Append("<section id=\"baselines\">\n<h2>Baselines</h2>\n");
        if (doc.Baselines.Count == 0)
        {
            sb.Append("<p>The spec names no baseline.</p>\n</section>\n");
            return;
        }

        sb.Append("<div class=\"scroll\"><table><thead><tr><th>Model</th><th>Held-out items</th><th>").Append(RateHead(doc)).Append("</th><th>ECE raw</th><th>Log loss raw</th><th>ECE calibrated</th><th>Log loss calibrated</th><th class=\"text\">Note</th></tr></thead><tbody>\n");
        foreach (var b in doc.Baselines)
        {
            sb.Append("<tr><td>").Append(E(b.Name)).Append(" (baseline)</td>");
            if (b.Missing is not null)
            {
                sb.Append("<td colspan=\"6\">n/a</td><td class=\"note\">").Append(E(b.Missing)).Append("</td></tr>\n");
                continue;
            }

            sb.Append("<td>").Append(Fmt.Int(b.HeldOutItems)).Append("</td><td>").Append(Fmt.Pct(b.Raw?.Accuracy)).Append("</td><td>").Append(Fmt.Num(b.Raw?.Ece))
                .Append("</td><td>").Append(Fmt.Num(b.Raw?.LogLoss)).Append("</td><td>").Append(Fmt.Num(b.Calibrated?.Ece)).Append("</td><td>").Append(Fmt.Num(b.Calibrated?.LogLoss))
                .Append("</td><td class=\"note\">").Append(E(b.CalibrationNote)).Append("</td></tr>\n");
        }

        foreach (var m in doc.Models.Where(m => m.RawHeldOut?.Metrics is not null))
        {
            var raw = m.RawHeldOut!.Metrics!;
            sb.Append("<tr><td>").Append(E(m.Model)).Append(" (Tau)</td><td>").Append(Fmt.Int(raw.N)).Append("</td><td>").Append(Fmt.Pct(raw.Accuracy))
                .Append("</td><td>").Append(Fmt.Num(raw.Ece)).Append("</td><td>").Append(Fmt.Num(raw.LogLoss)).Append("</td><td>").Append(Fmt.Num(m.BestCalibrated?.Ece))
                .Append("</td><td>").Append(Fmt.Num(m.BestCalibrated?.LogLoss)).Append("</td><td class=\"note\">for comparison</td></tr>\n");
        }

        sb.Append("</tbody></table></div>\n<p class=\"caption\">A classic small encoder, fine-tuned on the same training split, scored with exactly the same metric code as the Tau models. It is here to test the claim that a fixed task favours classic fine-tuning: where it beats a Tau model, that is listed under the misses, and it is a reason to prefer the classic model for this task.</p>\n</section>\n");
    }

    private static void ConfusionSection(StringBuilder sb, ReportDocument doc)
    {
        var views = doc.Models.Where(m => m.Confusion is not null).ToArray();
        sb.Append("<section id=\"confusion\">\n<h2>Where the models go wrong</h2>\n");
        if (views.Length == 0)
        {
            sb.Append("<p>No held-out measurement yet.</p>\n</section>\n");
            return;
        }

        if (views[0].Confusion!.Kind == "matrix")
        {
            sb.Append("<div class=\"multiples\">\n");
            foreach (var m in views)
            {
                sb.Append("<figure><figcaption>").Append(E($"{m.Model} ({m.Confusion!.Source}, {Fmt.Int(m.Confusion.N)} items)")).Append("</figcaption>")
                    .Append(SvgCharts.Matrix(m.Confusion, m.Model, doc.Reference)).Append("</figure>\n");
            }

            sb.Append("</div>\n<p class=\"caption\">Rows are ").Append(VsFrontier(doc) ? "the frontier model's answer" : "the gold answer").Append(" and columns the model's answer, so the diagonal is correct and everything else is a mistake; darker cells hold a larger share of their row. ")
                .Append(doc.QuestionType == "score"
                    ? "For an ordered scale, mistakes next to the diagonal are near misses and those far from it are serious; a model whose errors sit far off the diagonal should not be trusted on the extreme levels."
                    : "Mistakes that cluster in one cell point to two answers the model cannot tell apart, which is where better instructions or more training data would help most.")
                .Append("</p>\n");
            sb.Append("<details><summary>Table view: every cell</summary><div class=\"scroll\"><table><thead><tr><th>Model</th><th class=\"text\">")
                .Append(VsFrontier(doc) ? "Frontier" : "Gold").Append("</th>");
            foreach (var l in views[0].Confusion!.Labels)
            {
                sb.Append("<th>").Append(E(l)).Append("</th>");
            }

            sb.Append("</tr></thead><tbody>\n");
            foreach (var m in views)
            {
                for (int i = 0; i < m.Confusion!.Labels.Count; i++)
                {
                    sb.Append("<tr><td>").Append(E(m.Model)).Append("</td><td class=\"text\">").Append(E(m.Confusion.Labels[i])).Append("</td>");
                    foreach (var count in m.Confusion.Matrix![i])
                    {
                        sb.Append("<td>").Append(count).Append("</td>");
                    }

                    sb.Append("</tr>\n");
                }
            }

            sb.Append("</tbody></table></div></details>\n</section>\n");
            return;
        }

        foreach (var m in views)
        {
            sb.Append("<h3>").Append(E($"{m.Model}: most frequent confusions ({m.Confusion!.Source}, {Fmt.Int(m.Confusion.N)} items)")).Append("</h3>\n");
            sb.Append("<div class=\"scroll\"><table><thead><tr><th class=\"text\">").Append(VsFrontier(doc) ? "Frontier's answer" : "Gold answer")
                .Append("</th><th class=\"text\">Model's answer</th><th>Items</th></tr></thead><tbody>\n");
            foreach (var c in m.Confusion.Top!)
            {
                sb.Append("<tr><td class=\"text\">").Append(E(c.Gold)).Append("</td><td class=\"text\">").Append(E(c.Predicted)).Append("</td><td>").Append(c.Count).Append("</td></tr>\n");
            }

            sb.Append("</tbody></table></div>\n");
        }

        sb.Append("<p class=\"caption\">")
            .Append(E($"With {doc.Classes.Count} answers a full matrix is too large to read, so these tables list the 15 most frequent mistakes per model. Pairs that recur across models are usually answers whose descriptions overlap, which is where clearer option descriptions would help most."))
            .Append("</p>\n</section>\n");
    }
}
