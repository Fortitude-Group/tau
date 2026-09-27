using System.Text;
using Tau.Workbench.Cascade;
using Tau.Workbench.Frontier;
using Tau.Workbench.Measure;

namespace Tau.Workbench.Report;

/// <content>The result sections of the page.</content>
public static partial class HtmlReport
{
    private static int Slot(ReportDocument doc, string model)
    {
        int i = doc.Models.Select(m => m.Model).ToList().IndexOf(model);
        return (Math.Max(0, i) % SvgCharts.SeriesSlots) + 1;
    }

    /// <summary>A baseline's slot: after every model's, by its position in the spec.</summary>
    private static int BaselineSlot(ReportDocument doc, string baseline)
    {
        int i = doc.Baselines.Select(b => b.Name).ToList().IndexOf(baseline);
        return ((doc.Models.Count + Math.Max(0, i)) % SvgCharts.SeriesSlots) + 1;
    }

    /// <summary>How a baseline is labelled wherever it sits beside the Tau models in the cascade and cost tables.</summary>
    private const string BaselineLabel = "baseline, not served through Tau: local latency and energy not measured";

    private static void ModelsSection(StringBuilder sb, ReportDocument doc)
    {
        bool score = doc.QuestionType == "score";
        sb.Append("<section id=\"models\">\n<h2>").Append(VsFrontier(doc) ? "Agreement with the frontier model" : "Accuracy").Append(" and calibration, before and after</h2>\n<div class=\"scroll\"><table>\n<thead><tr><th>Model</th><th>Items</th><th>").Append(RateHead(doc)).Append(" raw</th><th>").Append(RateHead(doc)).Append(" calibrated</th><th>ECE raw</th><th>ECE calibrated</th><th>ECE change</th><th>Brier raw → cal.</th><th>Log loss raw → cal.</th>");
        if (score)
        {
            sb.Append("<th>MAE (levels) raw → cal.</th>");
        }

        sb.Append("<th class=\"text\">Calibrated view</th></tr></thead>\n<tbody>\n");
        foreach (var m in doc.Models)
        {
            var raw = m.RawHeldOut?.Metrics;
            var cal = m.BestCalibrated;
            sb.Append("<tr><td>").Append(E(m.Model)).Append("</td><td>").Append(raw is null ? "n/a" : Fmt.Int(raw.N))
                .Append("</td><td>").Append(Fmt.Pct(raw?.Accuracy)).Append("</td><td>").Append(Fmt.Pct(cal?.Accuracy))
                .Append("</td><td>").Append(Fmt.Num(raw?.Ece)).Append("</td><td>").Append(Fmt.Num(cal?.Ece))
                .Append("</td><td>").Append(m.EceReduction is { } r ? (r >= 0 ? "−" : "+") + Fmt.Pct(Math.Abs(r)) : "n/a")
                .Append("</td><td>").Append(Fmt.Num(raw?.Brier)).Append(" → ").Append(Fmt.Num(cal?.Brier))
                .Append("</td><td>").Append(Fmt.Num(raw?.LogLoss)).Append(" → ").Append(Fmt.Num(cal?.LogLoss)).Append("</td>");
            if (score)
            {
                sb.Append("<td>").Append(Fmt.Num(raw?.Mae, "0.00")).Append(" → ").Append(Fmt.Num(cal?.Mae, "0.00")).Append("</td>");
            }

            sb.Append("<td class=\"note\">").Append(m.BestCalibratedSource switch
            {
                Phases.Calibrated => "Runtime with calibrators loaded" + (m.RuntimeVsOfflineMaxDiff switch
                {
                    0 => "; matches the Workbench's offline calibration exactly",
                    { } d => $"; matches the Workbench's offline calibration to within {Fmt.Num(d, "0.0e0")} in any probability",
                    _ => "",
                }),
                Phases.Offline => "Workbench applied the calibrator offline; the Runtime's calibrated phase has not run",
                _ => "not calibrated yet",
            });
            if (m.Calibration is { } fit && Precisions.CalibratorNote(fit.RawPrecision) is { } precisionNote)
            {
                sb.Append(". ").Append(E(precisionNote));
            }

            sb.Append("</td></tr>\n");
        }

        sb.Append("</tbody></table></div>\n<p class=\"caption\">Held-out items only. ECE (expected calibration error) is the average gap between how confident a model is and how often it is right, over 15 confidence bins; lower is better and 0 is perfect. ")
            .Append(E(doc.Metadata.ConfidenceNote)).Append(' ');
        if (VsFrontier(doc))
        {
            sb.Append(E($"Here a model is right when its answer matches {doc.Metadata.FrontierModel}'s, so the {RateHead(doc).ToLowerInvariant()} columns are agreement with the frontier model, not accuracy, and ECE measures whether a model's confidence says when it agrees. ")); 
        }

        var calibrated = doc.Models.Where(m => m.EceReduction is not null).ToArray();
        if (calibrated.Length > 0)
        {
            var best = calibrated.MaxBy(m => m.EceReduction!.Value)!;
            var worst = calibrated.MinBy(m => m.EceReduction!.Value)!;
            sb.Append(E($"The goal is a 50% cut in ECE. The largest cut is {best.Model}'s ({Fmt.Pct(best.EceReduction)}) and the smallest is {worst.Model}'s ({Fmt.Pct(worst.EceReduction)}); a model whose confidence is still off after calibration should not be trusted to gate decisions on its own."));
        }
        else
        {
            sb.Append("No model has been calibrated yet, so only the raw figures can be read: a large ECE means the model's confidence should not be used to gate decisions until it is calibrated.");
        }

        sb.Append("</p>\n</section>\n");
    }

    private static void ReliabilitySection(StringBuilder sb, ReportDocument doc)
    {
        var withBins = doc.Models.Where(m => m.RawHeldOut?.Metrics is not null).ToArray();
        sb.Append("<section id=\"reliability\">\n<h2>Reliability diagrams</h2>\n");
        if (withBins.Length == 0)
        {
            sb.Append("<p>No held-out measurement yet.</p>\n</section>\n");
            return;
        }

        sb.Append("<div class=\"legend\"><span><span class=\"key k1\"></span>Raw</span><span><span class=\"key k2\"></span>Calibrated</span><span><span class=\"key ref\"></span>Perfect calibration</span></div>\n<div class=\"multiples\">\n");
        foreach (var m in withBins)
        {
            sb.Append("<figure><figcaption>").Append(E(m.Model)).Append("</figcaption>")
                .Append(SvgCharts.Reliability(m.RawHeldOut!.Metrics!.Reliability, m.BestCalibrated?.Reliability, m.Model, doc.Reference)).Append("</figure>\n");
        }

        sb.Append("</div>\n<p class=\"caption\">").Append(E($"Each point is one confidence bin on the held-out split: how confident the model was (across) against how often it was right (up){(VsFrontier(doc) ? ", where right means agreeing with the frontier model" : "")}.")).Append(" Points below the diagonal are overconfident, points above it underconfident. After calibration the points should sit close to the diagonal; where they don't, a threshold on that model's confidence will not deliver the ").Append(VsFrontier(doc) ? "agreement" : "accuracy").Append(" it promises.</p>\n");
        sb.Append("<details><summary>Table view: every bin</summary><div class=\"scroll\"><table><thead><tr><th>Model</th><th>Bin</th><th>Raw items</th><th>Raw confidence</th><th>Raw ").Append(RateHead(doc).ToLowerInvariant()).Append("</th><th>Cal. items</th><th>Cal. confidence</th><th>Cal. ").Append(RateHead(doc).ToLowerInvariant()).Append("</th></tr></thead><tbody>\n");
        foreach (var m in withBins)
        {
            var raw = m.RawHeldOut!.Metrics!.Reliability;
            var cal = m.BestCalibrated?.Reliability;
            for (int i = 0; i < raw.Count; i++)
            {
                var c = cal?[i];
                if (raw[i].Count == 0 && (c is null || c.Count == 0))
                {
                    continue;
                }

                sb.Append("<tr><td>").Append(E(m.Model)).Append("</td><td>").Append(Fmt.Num(raw[i].Lower, "0.00")).Append('–').Append(Fmt.Num(raw[i].Upper, "0.00"))
                    .Append("</td><td>").Append(raw[i].Count).Append("</td><td>").Append(Fmt.Num(raw[i].MeanConfidence)).Append("</td><td>").Append(Fmt.Num(raw[i].Accuracy))
                    .Append("</td><td>").Append(c?.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "n/a").Append("</td><td>").Append(Fmt.Num(c?.MeanConfidence))
                    .Append("</td><td>").Append(Fmt.Num(c?.Accuracy)).Append("</td></tr>\n");
            }
        }

        sb.Append("</tbody></table></div></details>\n</section>\n");
    }

    private static void CalibratorsSection(StringBuilder sb, ReportDocument doc)
    {
        var fitted = doc.Models.Where(m => m.Calibration is not null).ToArray();
        if (fitted.Length == 0)
        {
            return;
        }

        sb.Append("<section id=\"calibrators\">\n<h2>Calibrators fitted</h2>\n<div class=\"scroll\"><table><thead><tr><th>Model</th><th class=\"text\">Scope</th><th>Items</th><th>Chosen</th><th>Temperature T</th><th>Temperature ECE / log loss</th><th>Isotonic knots</th><th>Isotonic ECE / log loss</th><th>Before: ECE / log loss</th></tr></thead><tbody>\n");
        foreach (var m in fitted)
        {
            foreach (var c in m.Calibration!.Calibrators)
            {
                sb.Append("<tr><td>").Append(E(m.Model)).Append("</td><td class=\"text\">").Append(E(c.Scope)).Append("</td><td>").Append(Fmt.Int(c.N))
                    .Append("</td><td>").Append(E(c.Chosen)).Append("</td><td>").Append(Fmt.Num(c.Temperature.Temperature, "0.000"))
                    .Append("</td><td>").Append(Fmt.Num(c.Temperature.CalibrationEce)).Append(" / ").Append(Fmt.Num(c.Temperature.CalibrationLogLoss))
                    .Append("</td><td>").Append(c.Isotonic?.Knots?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "not fitted")
                    .Append("</td><td>").Append(c.Isotonic is null ? E(c.IsotonicSkipped ?? "n/a") : Fmt.Num(c.Isotonic.CalibrationEce) + " / " + Fmt.Num(c.Isotonic.CalibrationLogLoss))
                    .Append("</td><td>").Append(Fmt.Num(c.CalibrationEceBefore)).Append(" / ").Append(Fmt.Num(c.CalibrationLogLossBefore)).Append("</td></tr>\n");
            }
        }

        sb.Append("</tbody></table></div>\n<p class=\"caption\">Both methods are fitted on the calibration split's raw probabilities, and the one with the lower calibration-split log loss is written for the Runtime to load; the scores in this table are on the calibration split, so they show the fit, not the result (the held-out result is in the first table). Isotonic regression needs at least 200 items and falls back to temperature scaling below that.</p>\n");
        var notes = fitted.SelectMany(m => m.Calibration!.Notes.Select(n => $"{m.Model}: {n}")).Distinct().ToArray();
        if (notes.Length > 0)
        {
            sb.Append("<ul class=\"basis\">");
            foreach (var n in notes)
            {
                sb.Append("<li>").Append(E(n)).Append("</li>");
            }

            sb.Append("</ul>\n");
        }

        sb.Append("</section>\n");
    }

    private static void ThresholdSection(StringBuilder sb, ReportDocument doc)
    {
        sb.Append("<section id=\"threshold\">\n<h2>How much can stay local</h2>\n");
        var baselines = doc.Baselines.Where(b => b.Threshold is not null).ToArray();
        if (doc.Thresholds.Count == 0 && baselines.Length == 0)
        {
            sb.Append("<p>The threshold stage has not run yet.</p>\n</section>\n");
            return;
        }

        var series = doc.Thresholds.Select(t => (Name: t.Model, Result: t, Slot: Slot(doc, t.Model), Dashed: false))
            .Concat(baselines.Select(b => (Name: $"{b.Name} (baseline)", Result: b.Threshold!, Slot: BaselineSlot(doc, b.Name), Dashed: true)))
            .ToArray();
        sb.Append("<div class=\"legend\">");
        foreach (var s in series)
        {
            sb.Append("<span>").Append(SvgCharts.Key(s.Slot, s.Dashed)).Append(E(s.Name)).Append("</span>");
        }

        sb.Append("<span><span class=\"key ref\"></span>Target ").Append(RateHead(doc).ToLowerInvariant()).Append("</span></div>\n");
        sb.Append(SvgCharts.Tradeoff(series.Select(s => (s.Result, s.Slot, s.Dashed)).ToArray(), doc.TargetError, doc.Reference));
        sb.Append("<p class=\"caption\">").Append(E($"Each line shows, on held-out items, what happens as the confidence threshold τ rises: fewer decisions are kept local (moving left) and those kept are more often right (moving up){(VsFrontier(doc) ? ", where right means agreeing with the frontier model" : "")}. The dot marks the τ chosen on the calibration split for a {Fmt.Pct(doc.TargetError)} target {(VsFrontier(doc) ? "disagreement" : "error")}. A line that never reaches the target line means no threshold makes that model safe enough on its own at that target. Thresholds that keep fewer than {SvgCharts.MinCurveItems} items are left off the chart because a handful of items says little; every point is in report.json.{(baselines.Length > 0 ? " Dashed lines are baselines, not served through Tau, put through exactly the same threshold rule on their own calibration lines." : "")}")).Append("</p>\n");
        sb.Append("<div class=\"scroll\"><table><thead><tr><th>Model</th><th>Confidences from</th><th>τ</th><th>Kept local (calibration)</th><th>Kept local (held-out)</th><th>").Append(RateHead(doc)).Append(" on kept (held-out)</th><th class=\"text\">Result</th></tr></thead><tbody>\n");
        foreach (var (name, t, _, _) in series)
        {
            sb.Append("<tr><td>").Append(E(name)).Append("</td><td>").Append(E(t.Source)).Append("</td><td>").Append(Fmt.Num(t.Tau, "0.00"))
                .Append("</td><td>").Append(Fmt.Pct(t.CalibrationAcceptRate)).Append("</td><td>").Append(Fmt.Pct(t.HeldOutAcceptRate))
                .Append("</td><td>").Append(Fmt.Pct(t.HeldOutAcceptedAccuracy)).Append("</td><td class=\"note\">").Append(E(t.Note)).Append("</td></tr>\n");
        }

        sb.Append("</tbody></table></div>\n</section>\n");
    }

    private static void CascadeSection(StringBuilder sb, ReportDocument doc)
    {
        sb.Append("<section id=\"cascade\">\n<h2>Cascade: local first, frontier for the rest</h2>\n");
        if (doc.Cascades.Count == 0)
        {
            sb.Append("<p>The cascade stage has not run yet.</p>\n</section>\n");
            return;
        }

        bool vsFrontier = VsFrontier(doc);
        string suffix = vsFrontier ? " (agreement)" : "";
        sb.Append("<div class=\"scroll\"><table><thead><tr><th>Model</th><th>τ</th><th>Kept local</th><th>Escalated</th><th>No frontier answer</th><th>Local only")
            .Append(suffix).Append("</th><th>Frontier only</th><th>Cascade").Append(suffix).Append("</th><th>Local p50 latency</th></tr></thead><tbody>\n");
        var baselines = doc.Baselines.Where(b => b.Cascade is not null).Select(b => (Title: $"{b.Name} ({BaselineLabel})", Result: b.Cascade!, Baseline: true));
        var rows = doc.Cascades.Select(c => (Title: c.Model, Result: c, Baseline: false)).Concat(baselines).ToArray();
        foreach (var (title, c, baseline) in rows)
        {
            sb.Append("<tr><td").Append(baseline ? " class=\"text\"" : "").Append('>').Append(E(title)).Append("</td><td>").Append(Fmt.Num(c.Tau, "0.00")).Append("</td><td>").Append(Fmt.Pct(c.ShareLocal))
                .Append("</td><td>").Append(Fmt.Pct(c.ShareEscalated)).Append("</td><td>").Append(c.Tau is null ? "n/a" : Fmt.Int(c.MissingFrontier))
                .Append("</td><td>").Append(Rate(c.LocalOnlyAccuracy)).Append("</td><td>").Append(vsFrontier ? "100% by construction" : Rate(c.FrontierOnlyAccuracy))
                .Append("</td><td>").Append(c.BlendedAccuracy is null ? E(c.NotSimulated ?? "n/a") : Rate(c.BlendedAccuracy))
                .Append("</td><td>").Append(baseline ? "not measured" : c.LocalLatencyP50Ms is { } l ? Fmt.Num(l, "0.0") + " ms" : "n/a").Append("</td></tr>\n");
        }

        sb.Append("</tbody></table></div>\n<p class=\"caption\">")
            .Append(E(vsFrontier
                ? $"Every rate here is agreement with the frontier model on held-out items, not accuracy, with the number of items it covers in brackets. The cascade serves the local answer when its confidence is at least τ and the frontier model's cached answer otherwise; the cascade figure is the share of items where the served answer equals the frontier model's. {CascadeStage.FrontierOnlyByConstruction} The closer the cascade gets to 100% while keeping a large share local, the better the local model stands in for the frontier call. {doc.Cascades[0].FrontierLatencyNote}"
                : $"Accuracy is on held-out items, with the number of items each figure covers in brackets. The cascade keeps the local answer when its confidence is at least τ and uses the frontier model's cached answer otherwise; an escalated item with no cached answer is counted and left out, never guessed. If the cascade is close to frontier-only accuracy while keeping a large share local, most of the frontier cost can be avoided. {doc.Cascades[0].FrontierLatencyNote}"))
            .Append("</p>\n");

        foreach (var (title, c, _) in rows.Where(r => r.Result.Cost is not null))
        {
            CostTable(sb, title, c.Cost!);
        }

        foreach (var (_, c, _) in rows.Where(r => r.Result.Cost is null && r.Result.CostUnavailable is not null).Take(1))
        {
            sb.Append("<p class=\"caption\">").Append(E(c.CostUnavailable)).Append("</p>\n");
        }

        sb.Append("</section>\n");
    }

    private static void CostTable(StringBuilder sb, string title, CostEstimate cost)
    {
        sb.Append("<h3>").Append(E($"Cost for {title}")).Append(" <span class=\"estimate\">Estimates, not measured bills</span></h3>\n");
        sb.Append("<div class=\"scroll\"><table><thead><tr><th class=\"text\">Price basis</th><th>Frontier only, per million (USD)</th><th>Frontier only, per 1,000</th><th>Frontier only, per million</th><th>Cascade, per 1,000</th><th>Cascade, per million</th><th>Saving, per million</th></tr></thead><tbody>\n");
        foreach (var r in cost.Rows)
        {
            sb.Append("<tr><td class=\"text\">").Append(E(r.Label)).Append("</td><td>").Append(E(Fmt.Usd(r.FrontierOnlyUsdPerMillion)))
                .Append("</td><td>").Append(E(Fmt.Gbp(r.FrontierOnlyGbpPer1k))).Append("</td><td>").Append(E(Fmt.Gbp(r.FrontierOnlyGbpPerMillion)))
                .Append("</td><td>").Append(E(Fmt.Gbp(r.CascadeGbpPer1k))).Append("</td><td>").Append(E(Fmt.Gbp(r.CascadeGbpPerMillion)))
                .Append("</td><td>").Append(E(Fmt.Gbp(r.SavingGbpPerMillion))).Append("</td></tr>\n");
        }

        sb.Append("</tbody></table></div>\n");
        foreach (var refusal in new[] { cost.GbpRefused, cost.CascadeGbpRefused }.OfType<string>().Distinct())
        {
            sb.Append("<p class=\"caption\"><strong>").Append(E(refusal)).Append("</strong></p>\n");
        }

        sb.Append("<p class=\"caption\">Every figure in this table is an estimate from list prices and estimated token counts, not a measured bill. It shows what the same decisions would cost through the API, and what share of that the cascade avoids. Basis:</p>\n<ul class=\"basis\">");
        foreach (var b in cost.Basis)
        {
            sb.Append("<li>").Append(E(b)).Append("</li>");
        }

        sb.Append("</ul>\n");
    }

    private static string Rate(Frontier.CountedRate r) => r.Rate is null ? "n/a" : $"{Fmt.Pct(r.Rate)} ({Fmt.Int(r.N)})";
}
