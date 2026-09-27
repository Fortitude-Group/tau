using Tau.Calibration;
using Tau.Client;
using Tau.Contract;
using Tau.Workbench.Measure;

namespace Tau.Workbench;

/// <summary>
/// Temporary stand-ins for two APIs being added in another lane (tasks T041 and T042). Both are
/// extension methods with exactly the planned names and shapes, so once the real instance methods land
/// the compiler prefers them and these become dead code; delete this file at merge.
/// </summary>
internal static class MergeShims
{
    /// <summary>Probability floor before the log (research R-01).</summary>
    private const double ProbabilityFloor = 1e-6;

    // TODO(merge): replace with Tau.Calibration API — Calibrator.ApplyToProbabilities(double[]) (T041).
    /// <summary>
    /// Applies a calibrator to reference probabilities, per research R-01: clamp each probability at
    /// 1e-6, take the log, then run the existing <see cref="Calibrator.Apply"/> on those log values.
    /// </summary>
    /// <param name="calibrator">The calibrator.</param>
    /// <param name="referenceProbabilities">The model's reference (raw) probabilities, one per option.</param>
    public static double[] ApplyToProbabilities(this Calibrator calibrator, double[] referenceProbabilities)
    {
        ArgumentNullException.ThrowIfNull(calibrator);
        ArgumentNullException.ThrowIfNull(referenceProbabilities);
        var logs = new double[referenceProbabilities.Length];
        for (int i = 0; i < logs.Length; i++)
        {
            logs[i] = Math.Log(Math.Max(referenceProbabilities[i], ProbabilityFloor));
        }

        return calibrator.Apply(logs);
    }

    // TODO(merge): replace with Tau.Client API — SystemOneClient.SystemOneAsync(request, headers, ct) (T042).
    /// <summary>
    /// Sends a request with extra per-request headers. Until Tau.Client grows the overload, the headers are
    /// handed to <see cref="CaptureHandler"/> through the async flow and added to the outgoing message there.
    /// </summary>
    /// <param name="client">The client (its HttpClient must run through <see cref="CaptureHandler"/>).</param>
    /// <param name="request">The request.</param>
    /// <param name="headers">Headers to add to this request only.</param>
    /// <param name="ct">Cancellation.</param>
    public static async Task<DecisionResponse> SystemOneAsync(
        this SystemOneClient client, DecisionRequest request, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(headers);
        var capture = CaptureHandler.Current ?? throw new InvalidOperationException("SystemOneAsync with headers needs an active exchange capture.");
        foreach (var (name, value) in headers)
        {
            capture.RequestHeaders[name] = value;
        }

        return await client.SystemOneAsync(request, ct).ConfigureAwait(false);
    }
}
