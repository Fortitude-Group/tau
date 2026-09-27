namespace Tau.Calibration;

/// <summary>The kind of question a calibrator was fitted for, per <c>tau.calibrator</c> v1.</summary>
public enum QuestionType
{
    /// <summary>A multiple-choice question.</summary>
    Choice,

    /// <summary>A numeric score question.</summary>
    Score,

    /// <summary>A "none of the options are correct" (noul) question.</summary>
    Noul,
}

/// <summary>Wire (JSON) string conversions for <see cref="QuestionType"/>.</summary>
public static class QuestionTypeExtensions
{
    /// <summary>Converts to the wire representation used in <c>tau.calibrator</c> JSON files.</summary>
    public static string ToWireString(this QuestionType value) => value switch
    {
        QuestionType.Choice => "choice",
        QuestionType.Score => "score",
        QuestionType.Noul => "noul",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown question type."),
    };

    /// <summary>Parses the wire representation, or returns <see langword="null"/> if unrecognised.</summary>
    public static QuestionType? FromWireString(string value) => value switch
    {
        "choice" => QuestionType.Choice,
        "score" => QuestionType.Score,
        "noul" => QuestionType.Noul,
        _ => null,
    };
}
