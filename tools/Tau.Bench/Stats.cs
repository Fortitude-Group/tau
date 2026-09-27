namespace Tau.Bench;

/// <summary>Summary of one measured series, in milliseconds. Rounded to 4 decimals so reports diff cleanly.</summary>
internal sealed record Summary(
    int N, double Mean, double Sd, double Min, double P50, double P95, double P99, double Max,
    double P50PerQuestion, double MeanPerQuestion);

internal static class Stats
{
    public const string PercentileMethod =
        "linear interpolation between closest ranks (Hyndman-Fan type 7, the NumPy and Excel PERCENTILE.INC default)";

    /// <summary>Summarises <paramref name="samples"/>; per-question figures divide by <paramref name="questions"/>.</summary>
    public static Summary Summarise(IReadOnlyList<double> samples, int questions)
    {
        if (samples.Count == 0) throw new ArgumentException("no samples", nameof(samples));
        var sorted = samples.Order().ToArray();
        var n = sorted.Length;
        var mean = sorted.Average();
        var sd = n > 1 ? Math.Sqrt(sorted.Sum(x => (x - mean) * (x - mean)) / (n - 1)) : 0.0;
        var p50 = Percentile(sorted, 0.50);
        return new Summary(n, R(mean), R(sd), R(sorted[0]), R(p50), R(Percentile(sorted, 0.95)), R(Percentile(sorted, 0.99)),
            R(sorted[^1]), R(p50 / questions), R(mean / questions));
    }

    /// <summary>Type-7 percentile of an ascending array.</summary>
    public static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 1) return sorted[0];
        var h = (sorted.Length - 1) * p;
        var lo = (int)Math.Floor(h);
        var hi = Math.Min(lo + 1, sorted.Length - 1);
        return sorted[lo] + (h - lo) * (sorted[hi] - sorted[lo]);
    }

    /// <summary>Relative difference of the second value from the first, in percent (null when there's no second).</summary>
    public static double? RelativeDiffPercent(double first, double? second) =>
        second is { } s && first != 0 ? R((s - first) / first * 100.0) : null;

    public static double R(double x) => Math.Round(x, 4, MidpointRounding.AwayFromZero);
}
