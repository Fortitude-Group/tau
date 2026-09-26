namespace Tau.Calibration;

/// <summary>
/// An option-count bucket, matching the Laya reference bucketing: 2 options, 3-10 options,
/// 6-10 options, or 11+ options.
/// </summary>
public enum OptionBucket
{
    /// <summary>Exactly 2 options.</summary>
    Two,

    /// <summary>3 to 5 options.</summary>
    ThreeToFive,

    /// <summary>6 to 10 options.</summary>
    SixToTen,

    /// <summary>11 or more options.</summary>
    ElevenPlus,
}

/// <summary>Wire (JSON) string conversions and option-count bucketing for <see cref="OptionBucket"/>.</summary>
public static class OptionBucketExtensions
{
    /// <summary>Converts to the wire representation used in <c>tau.calibrator</c> JSON files.</summary>
    public static string ToWireString(this OptionBucket value) => value switch
    {
        OptionBucket.Two => "2",
        OptionBucket.ThreeToFive => "3-5",
        OptionBucket.SixToTen => "6-10",
        OptionBucket.ElevenPlus => "11+",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown option bucket."),
    };

    /// <summary>Parses the wire representation, or returns <see langword="null"/> if unrecognised.</summary>
    public static OptionBucket? FromWireString(string value) => value switch
    {
        "2" => OptionBucket.Two,
        "3-5" => OptionBucket.ThreeToFive,
        "6-10" => OptionBucket.SixToTen,
        "11+" => OptionBucket.ElevenPlus,
        _ => null,
    };

    /// <summary>
    /// Maps an option count to its bucket, per the Laya reference rule: <c>k &lt;= 2 =&gt; "2"</c>,
    /// <c>k &lt;= 5 =&gt; "3-5"</c>, <c>k &lt;= 10 =&gt; "6-10"</c>, else <c>"11+"</c>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="optionCount"/> is not positive.</exception>
    public static OptionBucket ForOptionCount(int optionCount)
    {
        if (optionCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(optionCount), optionCount, "Option count must be positive.");
        }

        if (optionCount <= 2)
        {
            return OptionBucket.Two;
        }

        if (optionCount <= 5)
        {
            return OptionBucket.ThreeToFive;
        }

        if (optionCount <= 10)
        {
            return OptionBucket.SixToTen;
        }

        return OptionBucket.ElevenPlus;
    }
}
