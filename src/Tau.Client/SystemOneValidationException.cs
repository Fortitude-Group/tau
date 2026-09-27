using Tau.Contract;

namespace Tau.Client;

/// <summary>
/// Thrown when a server rejects a <c>/v1/systemone</c> request as invalid (HTTP 422).
/// </summary>
public sealed class SystemOneValidationException : Exception
{
    /// <summary>
    /// Every problem the server reported. When the 422 body matches Tau's documented shape
    /// (<c>specs/001-runtime-onnx-parity/data-model.md</c>, <see cref="ValidationErrorBody"/>),
    /// this is the parsed <c>error.details</c> list. When the body is some other server's shape
    /// (for example Jev or Kev), this is a single entry with <see cref="ValidationProblem.Path"/>
    /// <c>"$"</c> and the raw body text as the problem.
    /// </summary>
    public IReadOnlyList<ValidationProblem> Problems { get; }

    /// <summary>The raw, unparsed 422 response body, kept for diagnostics regardless of whether it parsed.</summary>
    public string RawBody { get; }

    /// <summary>Creates a new <see cref="SystemOneValidationException"/>.</summary>
    /// <param name="problems">Every problem the server reported.</param>
    /// <param name="rawBody">The raw, unparsed 422 response body.</param>
    public SystemOneValidationException(IReadOnlyList<ValidationProblem> problems, string rawBody)
        : base(BuildMessage(problems))
    {
        Problems = problems;
        RawBody = rawBody;
    }

    private static string BuildMessage(IReadOnlyList<ValidationProblem> problems) =>
        problems.Count == 0
            ? "/v1/systemone rejected the request as invalid."
            : $"/v1/systemone rejected the request as invalid: {string.Join("; ", problems.Select(p => $"{p.Path}: {p.Problem}"))}";
}
