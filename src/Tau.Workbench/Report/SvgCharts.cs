using System.Globalization;
using System.Net;
using System.Text;
using Tau.Workbench.Measure;
using Tau.Workbench.Spec;
using Tau.Workbench.Threshold;

namespace Tau.Workbench.Report;

/// <summary>
/// Inline SVG charts for the report. Styling comes from CSS classes defined in the page (so light and
/// dark themes both work without scripts): hairline solid grids, 2px series lines, 8px markers with a
/// 2px surface ring, and categorical colours assigned by the entity's fixed position, never by rank.
/// Hover detail uses native SVG <c>&lt;title&gt;</c> tooltips; every chart also has a table view.
/// </summary>
internal static class SvgCharts
{
    /// <summary>How many categorical slots the page defines (the validated palette's order).</summary>
    public const int SeriesSlots = 8;

    /// <summary>Trade-off points keeping fewer items than this are too noisy to draw (they stay in report.json).</summary>
    public const int MinCurveItems = 10;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>A reliability diagram: accuracy against mean confidence per bin, raw and calibrated, with the diagonal.</summary>
    /// <param name="raw">Raw held-out bins.</param>
    /// <param name="calibrated">Calibrated held-out bins, or null.</param>
    /// <param name="label">The model id (for the accessible name).</param>
    /// <param name="reference">What a right answer is measured against (sets the wording: accuracy or agreement with the frontier model).</param>
    public static string Reliability(IReadOnlyList<ReliabilityBin> raw, IReadOnlyList<ReliabilityBin>? calibrated, string label, ReferenceKind reference = ReferenceKind.Gold)
    {
        var (rate, rateCap) = Words(reference);
        const double W = 300, H = 280, L = 46, R = 12, T = 12, B = 42;
        var plot = new Plot(L, T, W - L - R, H - T - B, 0, 1, 0, 1);
        var sb = Open(W, H, $"Reliability diagram for {label}: {rate} against confidence, before and after calibration");
        plot.Grid(sb, [0, 0.25, 0.5, 0.75, 1], [0, 0.25, 0.5, 0.75, 1], "0.00", "0.00");
        sb.Append(Inv, $"<line class=\"ref\" x1=\"{plot.X(0):0.#}\" y1=\"{plot.Y(0):0.#}\" x2=\"{plot.X(1):0.#}\" y2=\"{plot.Y(1):0.#}\"><title>Perfect calibration: {rate} equals confidence</title></line>");
        Series(sb, plot, raw, 1, "Raw", rate);
        if (calibrated is not null)
        {
            Series(sb, plot, calibrated, 2, "Calibrated", rate);
        }

        plot.AxisTitles(sb, "Confidence, max(p)", rateCap);
        return sb.Append("</svg>").ToString();
    }

    /// <summary>The trade-off curve: accuracy on locally kept items against the share kept, one line per model.</summary>
    /// <param name="results">Threshold results, in spec order.</param>
    /// <param name="slotOf">The categorical slot (1-based) for a model, fixed by its position in the spec.</param>
    /// <param name="targetError">The target error, drawn as a reference line at 1 − target.</param>
    /// <param name="reference">What a right answer is measured against (sets the wording: accuracy or agreement with the frontier model).</param>
    public static string Tradeoff(IReadOnlyList<ThresholdResult> results, Func<string, int> slotOf, double targetError, ReferenceKind reference = ReferenceKind.Gold)
    {
        var (word, rateCap) = Words(reference);
        const double W = 960, H = 360, L = 56, R = 110, T = 16, B = 46;
        double minAcc = results.SelectMany(r => r.HeldOutCurve).Where(p => p.AcceptedAccuracy is not null && p.Accepted >= MinCurveItems).Select(p => p.AcceptedAccuracy!.Value)
            .DefaultIfEmpty(0).Min();
        double yMin = Math.Max(0, Math.Floor(Math.Min(minAcc, 1 - targetError) * 10 - 0.5) / 10);
        var plot = new Plot(L, T, W - L - R, H - T - B, 0, 1, yMin, 1);
        var sb = Open(W, H, $"Trade-off curve: {word} on items kept local against the share kept local, per model, on held-out items");
        var yTicks = Enumerable.Range(0, 11).Select(i => yMin + ((1 - yMin) * i / 10)).Where((_, i) => i % 2 == 0).ToArray();
        plot.Grid(sb, [0, 0.2, 0.4, 0.6, 0.8, 1], yTicks, "0%", "0%");
        double target = 1 - targetError;
        sb.Append(Inv, $"<line class=\"ref\" x1=\"{plot.X(0):0.#}\" y1=\"{plot.Y(target):0.#}\" x2=\"{plot.X(1):0.#}\" y2=\"{plot.Y(target):0.#}\"><title>Target: {Fmt.Pct(target)} {word} on kept items</title></line>");
        sb.Append(Inv, $"<text class=\"tick\" x=\"{plot.X(1) + 6:0.#}\" y=\"{plot.Y(target) + 4:0.#}\">target {Fmt.Pct(target, 0)}</text>");
        foreach (var r in results)
        {
            int slot = slotOf(r.Model);
            var points = r.HeldOutCurve.Where(p => p.AcceptedAccuracy is not null && p.Accepted >= MinCurveItems).ToArray();
            if (points.Length == 0)
            {
                continue;
            }

            var path = string.Join(" ", points.Select((p, i) => string.Create(Inv, $"{(i == 0 ? 'M' : 'L')}{plot.X(p.AcceptRate):0.#},{plot.Y(p.AcceptedAccuracy!.Value):0.#}")));
            sb.Append(Inv, $"<path class=\"line s{slot}\" d=\"{path}\"><title>{Esc(r.Model)}</title></path>");
            foreach (var p in points.Where((_, i) => i % 5 == 0))
            {
                sb.Append(Inv, $"<circle class=\"hit\" cx=\"{plot.X(p.AcceptRate):0.#}\" cy=\"{plot.Y(p.AcceptedAccuracy!.Value):0.#}\" r=\"8\"><title>{Esc(r.Model)}: τ = {p.Tau:0.00}, keeps {Fmt.Pct(p.AcceptRate)} local, {Fmt.Pct(p.AcceptedAccuracy)} {word} on those</title></circle>");
            }

            // Lines converge, so series are named by the legend above the chart rather than end labels.
            if (r.Tau is { } tau && r.HeldOutAcceptedAccuracy is { } acc && r.HeldOutAcceptRate is { } rate)
            {
                sb.Append(Inv, $"<circle class=\"dot s{slot}\" cx=\"{plot.X(rate):0.#}\" cy=\"{plot.Y(acc):0.#}\" r=\"5\"><title>{Esc(r.Model)}: chosen τ = {tau:0.00}; held-out keeps {Fmt.Pct(rate)} local at {Fmt.Pct(acc)} {word}</title></circle>");
                sb.Append(Inv, $"<text class=\"label\" x=\"{plot.X(rate) + 8:0.#}\" y=\"{plot.Y(acc) - 8:0.#}\">τ {tau:0.00}</text>");
            }
        }

        plot.AxisTitles(sb, "Share of decisions kept local (confidence ≥ τ)", $"{rateCap} on kept items");
        return sb.Append("</svg>").ToString();
    }

    /// <summary>A confusion matrix heat map (rows the reference label, columns predicted), shaded by row share.</summary>
    /// <param name="view">The matrix view.</param>
    /// <param name="label">The model id.</param>
    /// <param name="reference">What a right answer is measured against (sets the wording: accuracy or agreement with the frontier model).</param>
    public static string Matrix(ConfusionView view, string label, ReferenceKind reference = ReferenceKind.Gold)
    {
        string row0 = reference == ReferenceKind.Frontier ? "frontier answer" : "gold label";
        string rowShort = reference == ReferenceKind.Frontier ? "frontier" : "gold";
        int k = view.Labels.Count;
        double cell = k <= 5 ? 44 : 30, left = 110, top = 34;
        double w = left + (cell * k) + 8, h = top + (cell * k) + 30;
        var sb = Open(w, h, $"Confusion matrix for {label}: rows are the {row0}, columns the model's answer");
        sb.Append(Inv, $"<text class=\"tick\" x=\"{left:0.#}\" y=\"12\">answered →</text>");
        for (int j = 0; j < k; j++)
        {
            sb.Append(Inv, $"<text class=\"tick\" text-anchor=\"middle\" x=\"{left + (cell * j) + (cell / 2):0.#}\" y=\"{top - 8:0.#}\">{Esc(Short(view.Labels[j], 6))}<title>{Esc(view.Labels[j])}</title></text>");
        }

        for (int i = 0; i < k; i++)
        {
            var row = view.Matrix![i];
            int total = row.Sum();
            sb.Append(Inv, $"<text class=\"tick\" text-anchor=\"end\" x=\"{left - 8:0.#}\" y=\"{top + (cell * i) + (cell / 2) + 4:0.#}\">{Esc(Short(view.Labels[i], 14))}<title>{rowShort}: {Esc(view.Labels[i])}</title></text>");
            for (int j = 0; j < k; j++)
            {
                double share = total == 0 ? 0 : (double)row[j] / total;
                int step = share <= 0 ? 0 : Math.Min(6, 1 + (int)(share * 6));
                double x = left + (cell * j), y = top + (cell * i);
                sb.Append(Inv, $"<rect class=\"q{step}\" x=\"{x + 1:0.#}\" y=\"{y + 1:0.#}\" width=\"{cell - 2:0.#}\" height=\"{cell - 2:0.#}\" rx=\"2\"><title>{rowShort} {Esc(view.Labels[i])}, answered {Esc(view.Labels[j])}: {row[j]} of {total} ({Fmt.Pct(share)})</title></rect>");
                if (row[j] > 0)
                {
                    sb.Append(Inv, $"<text class=\"{(step >= 4 ? "cell-dark" : "cell")}\" text-anchor=\"middle\" x=\"{x + (cell / 2):0.#}\" y=\"{y + (cell / 2) + 4:0.#}\">{row[j]}</text>");
                }
            }
        }

        sb.Append(Inv, $"<text class=\"tick\" x=\"4\" y=\"{h - 8:0.#}\">rows: {row0}; shade: share of the row</text>");
        return sb.Append("</svg>").ToString();
    }

    private static void Series(StringBuilder sb, Plot plot, IReadOnlyList<ReliabilityBin> bins, int slot, string name, string rate)
    {
        var used = bins.Where(b => b.Count > 0 && b.MeanConfidence is not null && b.Accuracy is not null).ToArray();
        if (used.Length == 0)
        {
            return;
        }

        var path = string.Join(" ", used.Select((b, i) => string.Create(Inv, $"{(i == 0 ? 'M' : 'L')}{plot.X(b.MeanConfidence!.Value):0.#},{plot.Y(b.Accuracy!.Value):0.#}")));
        sb.Append(Inv, $"<path class=\"line s{slot}\" d=\"{path}\"/>");
        foreach (var b in used)
        {
            sb.Append(Inv, $"<circle class=\"dot s{slot}\" cx=\"{plot.X(b.MeanConfidence!.Value):0.#}\" cy=\"{plot.Y(b.Accuracy!.Value):0.#}\" r=\"4\"><title>{name}: confidence {b.Lower:0.00} to {b.Upper:0.00}, {b.Count} items, mean confidence {b.MeanConfidence:0.000}, {rate} {b.Accuracy:0.000}</title></circle>");
        }
    }

    /// <summary>The words for a right answer: accuracy against gold, or agreement with the frontier model.</summary>
    private static (string Rate, string RateCap) Words(ReferenceKind reference) => reference == ReferenceKind.Frontier
        ? ("agreement with the frontier model", "Agreement with the frontier model")
        : ("accuracy", "Accuracy");

    private static StringBuilder Open(double w, double h, string title)
    {
        var sb = new StringBuilder();
        // Inline SVG in HTML needs no xmlns, and leaving it out keeps the page free of any URL.
        sb.Append(Inv, $"<svg viewBox=\"0 0 {w:0.#} {h:0.#}\" width=\"{w:0.#}\" height=\"{h:0.#}\" role=\"img\" aria-label=\"{Esc(title)}\">");
        return sb;
    }

    internal static string Esc(string s) => WebUtility.HtmlEncode(s);

    internal static string Short(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    /// <summary>Maps data coordinates into a plot rectangle and draws its grid and axes.</summary>
    private sealed record Plot(double Left, double Top, double Width, double Height, double XMin, double XMax, double YMin, double YMax)
    {
        public double X(double v) => Left + ((v - XMin) / (XMax - XMin) * Width);

        public double Y(double v) => Top + Height - ((v - YMin) / (YMax - YMin) * Height);

        public void Grid(StringBuilder sb, IReadOnlyList<double> xTicks, IReadOnlyList<double> yTicks, string xFormat, string yFormat)
        {
            foreach (var t in yTicks)
            {
                sb.Append(Inv, $"<line class=\"grid\" x1=\"{Left:0.#}\" y1=\"{Y(t):0.#}\" x2=\"{Left + Width:0.#}\" y2=\"{Y(t):0.#}\"/>");
                sb.Append(Inv, $"<text class=\"tick\" text-anchor=\"end\" x=\"{Left - 6:0.#}\" y=\"{Y(t) + 4:0.#}\">{Format(t, yFormat)}</text>");
            }

            foreach (var t in xTicks)
            {
                sb.Append(Inv, $"<text class=\"tick\" text-anchor=\"middle\" x=\"{X(t):0.#}\" y=\"{Top + Height + 16:0.#}\">{Format(t, xFormat)}</text>");
            }

            sb.Append(Inv, $"<line class=\"axis\" x1=\"{Left:0.#}\" y1=\"{Top + Height:0.#}\" x2=\"{Left + Width:0.#}\" y2=\"{Top + Height:0.#}\"/>");
        }

        public void AxisTitles(StringBuilder sb, string x, string y)
        {
            sb.Append(Inv, $"<text class=\"axis-title\" text-anchor=\"middle\" x=\"{Left + (Width / 2):0.#}\" y=\"{Top + Height + 36:0.#}\">{Esc(x)}</text>");
            sb.Append(Inv, $"<text class=\"axis-title\" text-anchor=\"middle\" transform=\"translate(12 {Top + (Height / 2):0.#}) rotate(-90)\">{Esc(y)}</text>");
        }

        private static string Format(double v, string format) => format == "0%" ? Fmt.Pct(v, 0) : v.ToString(format, Inv);
    }
}
