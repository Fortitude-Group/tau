namespace Tau.Calibration;

/// <summary>The calibration method a calibrator applies, per <c>tau.calibrator</c> v1.</summary>
public enum CalibrationMethod
{
    /// <summary>Divide raw logits by a fitted temperature, then softmax.</summary>
    Temperature,

    /// <summary>Softmax raw logits, then map each option probability through a fitted isotonic function.</summary>
    Isotonic,
}

/// <summary>Wire (JSON) string conversions for <see cref="CalibrationMethod"/>.</summary>
public static class CalibrationMethodExtensions
{
    /// <summary>Converts to the wire representation used in <c>tau.calibrator</c> JSON files.</summary>
    public static string ToWireString(this CalibrationMethod value) => value switch
    {
        CalibrationMethod.Temperature => "temperature",
        CalibrationMethod.Isotonic => "isotonic",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown calibration method."),
    };

    /// <summary>Parses the wire representation, or returns <see langword="null"/> if unrecognised.</summary>
    public static CalibrationMethod? FromWireString(string value) => value switch
    {
        "temperature" => CalibrationMethod.Temperature,
        "isotonic" => CalibrationMethod.Isotonic,
        _ => null,
    };
}
