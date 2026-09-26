namespace Tau.Calibration;

/// <summary>
/// Thrown when a <c>tau.calibrator</c> JSON document (or a set of them) fails validation: an
/// unsupported format/version, a malformed field, an inconsistent isotonic function, a
/// model-hash mismatch, or a duplicate calibrator.
/// </summary>
public sealed class CalibratorValidationException : Exception
{
    /// <summary>Creates an exception naming the offending source (typically a file path) and the problem.</summary>
    /// <param name="source">The file path (or other identifier) of the offending document.</param>
    /// <param name="problem">A human-readable description of what is wrong.</param>
    public CalibratorValidationException(string source, string problem)
        : base($"Calibrator '{source}': {problem}")
    {
        SourceName = source;
        Problem = problem;
    }

    /// <summary>The file path (or other identifier) of the offending document.</summary>
    public string SourceName { get; }

    /// <summary>A human-readable description of what is wrong.</summary>
    public string Problem { get; }
}
