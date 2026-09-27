namespace Tau.Workbench;

/// <summary>
/// A Workbench failure with a message written for the person running <c>tau</c>: what went wrong and,
/// where there is one, what to do about it. The CLI maps it to exit code 1.
/// </summary>
public class WorkbenchException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">A plain-English description of the problem and the fix.</param>
    public WorkbenchException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception, wrapping the failure that caused it.</summary>
    /// <param name="message">A plain-English description of the problem and the fix.</param>
    /// <param name="innerException">The underlying failure.</param>
    public WorkbenchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// A stage could not finish because something outside the Workbench has to happen first (for example
/// frontier answers are pending, or the Runtime has not loaded the calibrators). The CLI maps it to
/// exit code 2.
/// </summary>
public sealed class StageBlockedException : WorkbenchException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What is blocking the stage and what to do next.</param>
    public StageBlockedException(string message)
        : base(message)
    {
    }
}

/// <summary>A decision spec that fails validation. Every problem found is listed.</summary>
public sealed class SpecValidationException : WorkbenchException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="path">The spec file.</param>
    /// <param name="problems">Every problem found.</param>
    public SpecValidationException(string path, IReadOnlyList<string> problems)
        : base($"{path} is not a valid decision spec:{Environment.NewLine}  - {string.Join(Environment.NewLine + "  - ", problems)}")
    {
        Path = path;
        Problems = problems;
    }

    /// <summary>The spec file.</summary>
    public string Path { get; }

    /// <summary>Every problem found.</summary>
    public IReadOnlyList<string> Problems { get; }
}
