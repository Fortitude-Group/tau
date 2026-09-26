namespace Tau.Calibration;

/// <summary>
/// A fitted (or directly supplied) non-decreasing step/piecewise-linear function, produced by
/// pool-adjacent-violators (PAV) isotonic regression.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Fit"/> runs PAV over points sorted by <c>x</c> and returns one knot per input point
/// (ties in <c>x</c> are not collapsed): the knot's <c>y</c> is the pooled (weighted-average) value
/// for the block that point ends up in. This mirrors the reference PAV behaviour, e.g. fitting
/// <c>y = [1, 3, 2, 4]</c> with equal weights yields <c>[1, 2.5, 2.5, 4]</c>.
/// </para>
/// <para>
/// <see cref="Apply"/> performs piecewise-linear interpolation between consecutive knots and
/// clamps to the end knot values outside the fitted range.
/// </para>
/// </remarks>
public sealed class IsotonicRegression
{
    private IsotonicRegression(double[] x, double[] y)
    {
        X = x;
        Y = y;
    }

    /// <summary>The knot x-coordinates, ascending.</summary>
    public IReadOnlyList<double> X { get; }

    /// <summary>The knot y-coordinates, non-decreasing.</summary>
    public IReadOnlyList<double> Y { get; }

    /// <summary>
    /// Wraps a pre-computed, already-valid set of knots (for example one loaded from a
    /// <see cref="CalibratorFile"/>) without re-running PAV.
    /// </summary>
    /// <param name="x">Knot x-coordinates, ascending.</param>
    /// <param name="y">Knot y-coordinates, non-decreasing.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when the arrays differ in length, have fewer than two points, <paramref name="x"/> is
    /// not ascending, or <paramref name="y"/> is not non-decreasing.
    /// </exception>
    public static IsotonicRegression FromKnots(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        if (x.Count != y.Count)
        {
            throw new ArgumentException("x and y must have the same length.", nameof(y));
        }

        if (x.Count < 2)
        {
            throw new ArgumentException("At least two knots are required.", nameof(x));
        }

        for (int i = 1; i < x.Count; i++)
        {
            if (x[i] < x[i - 1])
            {
                throw new ArgumentException("x must be ascending.", nameof(x));
            }

            if (y[i] < y[i - 1])
            {
                throw new ArgumentException("y must be non-decreasing.", nameof(y));
            }
        }

        return new IsotonicRegression(x.ToArray(), y.ToArray());
    }

    /// <summary>
    /// Fits a non-decreasing function to <paramref name="x"/>/<paramref name="y"/> using
    /// pool-adjacent-violators (PAV).
    /// </summary>
    /// <param name="x">Sample x-coordinates. Sorted ascending internally if not already sorted.</param>
    /// <param name="y">Sample y-coordinates (targets), same length as <paramref name="x"/>.</param>
    /// <param name="weights">
    /// Optional per-sample weights, same length as <paramref name="x"/>. When omitted, every sample
    /// has weight 1.
    /// </param>
    /// <returns>
    /// A fitted <see cref="IsotonicRegression"/> whose knots are one-per-input-point (in <c>x</c>
    /// order), pooled per PAV.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when array lengths mismatch, fewer than one point is supplied, or a weight is not
    /// strictly positive.
    /// </exception>
    public static IsotonicRegression Fit(IReadOnlyList<double> x, IReadOnlyList<double> y, IReadOnlyList<double>? weights = null)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        if (x.Count != y.Count)
        {
            throw new ArgumentException("x and y must have the same length.", nameof(y));
        }

        if (x.Count == 0)
        {
            throw new ArgumentException("At least one point is required.", nameof(x));
        }

        if (weights is not null && weights.Count != x.Count)
        {
            throw new ArgumentException("weights must have the same length as x.", nameof(weights));
        }

        int n = x.Count;
        var order = Enumerable.Range(0, n).ToArray();
        Array.Sort(order, (a, b) => x[a].CompareTo(x[b]));

        var sortedX = new double[n];
        var sortedY = new double[n];
        var sortedW = new double[n];
        for (int i = 0; i < n; i++)
        {
            int j = order[i];
            sortedX[i] = x[j];
            sortedY[i] = y[j];
            double w = weights is null ? 1.0 : weights[j];
            if (w <= 0)
            {
                throw new ArgumentException("weights must be strictly positive.", nameof(weights));
            }

            sortedW[i] = w;
        }

        // PAV via a stack of pooled blocks. Each block records the sum of weighted y, the sum of
        // weights, and which (sorted-order) indices it covers.
        var blockSumWy = new List<double>(n);
        var blockSumW = new List<double>(n);
        var blockStart = new List<int>(n);
        var blockEnd = new List<int>(n);

        for (int i = 0; i < n; i++)
        {
            blockSumWy.Add(sortedY[i] * sortedW[i]);
            blockSumW.Add(sortedW[i]);
            blockStart.Add(i);
            blockEnd.Add(i);

            while (blockSumWy.Count >= 2)
            {
                int last = blockSumWy.Count - 1;
                double avgLast = blockSumWy[last] / blockSumW[last];
                double avgPrev = blockSumWy[last - 1] / blockSumW[last - 1];
                if (avgPrev <= avgLast)
                {
                    break;
                }

                blockSumWy[last - 1] += blockSumWy[last];
                blockSumW[last - 1] += blockSumW[last];
                blockEnd[last - 1] = blockEnd[last];
                blockSumWy.RemoveAt(last);
                blockSumW.RemoveAt(last);
                blockStart.RemoveAt(last);
                blockEnd.RemoveAt(last);
            }
        }

        var fittedY = new double[n];
        for (int b = 0; b < blockSumWy.Count; b++)
        {
            double value = blockSumWy[b] / blockSumW[b];
            for (int i = blockStart[b]; i <= blockEnd[b]; i++)
            {
                fittedY[i] = value;
            }
        }

        return new IsotonicRegression(sortedX, fittedY);
    }

    /// <summary>
    /// Evaluates the fitted function at <paramref name="p"/> by piecewise-linear interpolation
    /// between the surrounding knots, clamping to the first/last knot's <c>y</c> outside the
    /// fitted <c>x</c> range.
    /// </summary>
    public double Apply(double p)
    {
        if (p <= X[0])
        {
            return Y[0];
        }

        int lastIndex = X.Count - 1;
        if (p >= X[lastIndex])
        {
            return Y[lastIndex];
        }

        // Binary search for the interval [X[lo], X[hi]] containing p.
        int lo = 0;
        int hi = lastIndex;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (X[mid] <= p)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        double x0 = X[lo];
        double x1 = X[hi];
        double y0 = Y[lo];
        double y1 = Y[hi];
        if (x1 == x0)
        {
            return y0;
        }

        double t = (p - x0) / (x1 - x0);
        return y0 + (t * (y1 - y0));
    }
}
