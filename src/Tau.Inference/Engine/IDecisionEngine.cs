using Tau.Contract;

namespace Tau.Inference.Engine;

/// <summary>Per-request options that sit outside the contract (carried in <c>x-tau-*</c> headers).</summary>
/// <param name="Raw">Skip Tau calibrators and return the reference post-processing (<c>x-tau-raw: true</c>).</param>
public sealed record DecisionOptions(bool Raw = false);

/// <summary>Diagnostics for one answered request: telemetry and <c>x-tau-*</c> response headers.</summary>
/// <param name="ModelId">Resolved Tau model id.</param>
/// <param name="ModelSha256">ONNX sha256 of the model that answered.</param>
/// <param name="RouteReason">Why this model was chosen.</param>
/// <param name="Calibrators">Ids of the Tau calibrators applied (empty when none or raw).</param>
/// <param name="Truncated">Whether any row's state was truncated to fit the model.</param>
/// <param name="BatchRows">Rows sent to the model in the request's single forward pass.</param>
/// <param name="InputTokens">Tokens processed (sum of attention masks).</param>
/// <param name="ModelMilliseconds">Time spent in the model forward pass.</param>
public sealed record DecisionDiagnostics(
    string ModelId, string ModelSha256, string RouteReason, IReadOnlyList<string> Calibrators,
    bool Truncated, int BatchRows, int InputTokens, double ModelMilliseconds);

/// <summary>A contract response plus its diagnostics.</summary>
/// <param name="Response">The contract response.</param>
/// <param name="Diagnostics">Diagnostics.</param>
public sealed record DecisionResult(DecisionResponse Response, DecisionDiagnostics Diagnostics);

/// <summary>
/// A request that is valid under the contract but can't be answered faithfully by the chosen model
/// (options over the token budget, an unknown model id, a shape the model's reference rejects).
/// The host returns it as a 422 with the same body shape as a contract violation.
/// </summary>
public sealed class DecisionRejectedException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="problems">What's wrong, with JSON paths.</param>
    public DecisionRejectedException(IReadOnlyList<ValidationProblem> problems)
        : base(string.Join("; ", problems.Select(p => $"{p.Path}: {p.Problem}"))) => Problems = problems;

    /// <summary>Creates the exception for one problem.</summary>
    /// <param name="path">JSON path of the offending field.</param>
    /// <param name="problem">What's wrong.</param>
    public DecisionRejectedException(string path, string problem) : this([new ValidationProblem(path, problem)]) { }

    /// <summary>The problems.</summary>
    public IReadOnlyList<ValidationProblem> Problems { get; }
}

/// <summary>Information about an installed model, for <c>GET /v1/models</c>.</summary>
/// <param name="Id">Tau model id.</param>
/// <param name="Family">laya or von.</param>
/// <param name="Revision">Pinned upstream commit.</param>
/// <param name="OnnxSha256">ONNX sha256.</param>
/// <param name="Loaded">Whether its inference session is resident.</param>
public sealed record InstalledModel(string Id, string Family, string Revision, string OnnxSha256, bool Loaded);

/// <summary>Answers contract requests. The HTTP host is a thin consumer of this (constitution Principle I).</summary>
public interface IDecisionEngine
{
    /// <summary>Answers a validated request.</summary>
    /// <param name="request">The parsed, contract-valid request.</param>
    /// <param name="options">Tau options for this request.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <exception cref="DecisionRejectedException">The request can't be answered faithfully (maps to 422).</exception>
    Task<DecisionResult> DecideAsync(DecisionRequest request, DecisionOptions options, CancellationToken cancellationToken);

    /// <summary>Installed models.</summary>
    IReadOnlyList<InstalledModel> Models { get; }

    /// <summary>Auto-route aliases the engine accepts as a model id.</summary>
    IReadOnlyList<string> Aliases { get; }

    /// <summary>True once every configured model is loaded and ready.</summary>
    bool Ready { get; }
}
