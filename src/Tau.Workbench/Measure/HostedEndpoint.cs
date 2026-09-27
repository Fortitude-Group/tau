using Tau.Contract;
using Tau.Workbench.Spec;

namespace Tau.Workbench.Measure;

/// <summary>Reads a hosted endpoint's API key at run time. The key is never written anywhere.</summary>
public static class ApiKeys
{
    /// <summary>The real environment: <see cref="Environment.GetEnvironmentVariable(string, EnvironmentVariableTarget)"/>.</summary>
    public static string? Environment(string name, EnvironmentVariableTarget target) =>
        System.Environment.GetEnvironmentVariable(name, target);

    /// <summary>
    /// Reads the key from the process environment first, then the Windows User scope (where <c>setx</c> writes, so a
    /// key set after this shell started is still found). Returns null when neither holds a non-blank value.
    /// </summary>
    /// <param name="variable">The environment variable's name (from the spec's <c>api_key_env</c>).</param>
    /// <param name="read">The environment reader; tests pass a fake.</param>
    public static string? Read(string variable, Func<string, EnvironmentVariableTarget, string?> read)
    {
        ArgumentNullException.ThrowIfNull(variable);
        ArgumentNullException.ThrowIfNull(read);
        foreach (var target in new[] { EnvironmentVariableTarget.Process, EnvironmentVariableTarget.User })
        {
            if (read(variable, target) is { } value && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}

/// <summary>
/// The hard spend limit on a hosted endpoint. Before each request it projects the spend: what the responses so far
/// cost (their <c>usage</c> × the spec's price), plus the average cost per request for every request still in
/// flight, plus the average × 2 as a margin. When that would exceed the budget, it refuses the request and every
/// later one. Thread-safe: concurrent measure tasks share one guard per hosted model per measure run.
/// </summary>
public sealed class SpendGuard
{
    private readonly object _gate = new();
    private readonly ExternalModelSpec _spec;
    private int _inFlight;

    /// <summary>Creates a guard for one hosted endpoint.</summary>
    /// <param name="spec">The hosted endpoint (its price and budget).</param>
    public SpendGuard(ExternalModelSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        _spec = spec;
    }

    /// <summary>The budget, USD.</summary>
    public double BudgetUsd => _spec.BudgetUsd;

    /// <summary>Estimated spend so far, USD.</summary>
    public double SpentUsd { get; private set; }

    /// <summary>Responses that reported usage.</summary>
    public int Priced { get; private set; }

    /// <summary>Input tokens reported so far.</summary>
    public long InputTokens { get; private set; }

    /// <summary>Output tokens reported so far.</summary>
    public long OutputTokens { get; private set; }

    /// <summary>True once the guard has refused a request.</summary>
    public bool Stopped { get; private set; }

    /// <summary>The average estimated cost of a priced request, USD (0 before any response).</summary>
    public double AverageUsd => Priced == 0 ? 0 : SpentUsd / Priced;

    /// <summary>Asks to send one request. False means the budget would be exceeded, and the guard stays stopped.</summary>
    public bool TryStart()
    {
        lock (_gate)
        {
            if (Stopped || SpentUsd + (AverageUsd * (_inFlight + 2)) > _spec.BudgetUsd)
            {
                Stopped = true;
                return false;
            }

            _inFlight++;
            return true;
        }
    }

    /// <summary>Records a finished request, priced from its usage when it has one.</summary>
    /// <param name="usage">The response's usage, or null for a failed call (not priced).</param>
    public void Finish(Usage? usage)
    {
        lock (_gate)
        {
            _inFlight--;
            if (usage is null)
            {
                return;
            }

            Priced++;
            InputTokens += usage.InputTokens;
            OutputTokens += usage.OutputTokens;
            SpentUsd += _spec.CostUsd(usage.InputTokens, usage.OutputTokens);
        }
    }
}

/// <summary>A hosted endpoint's measure run: its spec and the spend guard shared by every split of the run.</summary>
/// <param name="Spec">The hosted endpoint.</param>
/// <param name="Guard">The spend guard.</param>
public sealed record HostedRun(ExternalModelSpec Spec, SpendGuard Guard);

/// <summary>What a measure summary records about a hosted endpoint (null for local models).</summary>
public sealed record HostedMeasure
{
    /// <summary>The label every report uses.</summary>
    public string Label { get; init; } = ExternalModelSpec.Label;

    /// <summary>The endpoint URL.</summary>
    public required string Endpoint { get; init; }

    /// <summary>The request's <c>model</c> field.</summary>
    public required string RequestedModel { get; init; }

    /// <summary>
    /// The distinct <c>model</c> strings the responses returned (for example <c>jev-1.13.0</c>): the endpoint's only
    /// identity, since a hosted endpoint has no <c>/v1/models</c> hash.
    /// </summary>
    public required IReadOnlyList<string> ModelsReturned { get; init; }

    /// <summary>Input tokens the responses in this split reported.</summary>
    public required long InputTokens { get; init; }

    /// <summary>Output tokens the responses in this split reported.</summary>
    public required long OutputTokens { get; init; }

    /// <summary>Responses in this split that reported usage.</summary>
    public required int PricedResponses { get; init; }

    /// <summary>Estimated spend on this split, USD (usage × the spec's price).</summary>
    public required double EstimatedSpendUsd { get; init; }

    /// <summary>Estimated spend of the whole measure run so far (every split before and including this one), USD.</summary>
    public required double RunSpendUsd { get; init; }

    /// <summary>The budget of the measure run, USD.</summary>
    public required double BudgetUsd { get; init; }

    /// <summary>Input price used, USD per million tokens.</summary>
    public required double InputUsdPerMTok { get; init; }

    /// <summary>Output price used, USD per million tokens.</summary>
    public required double OutputUsdPerMTok { get; init; }

    /// <summary>True when the spend guard stopped the run during this split.</summary>
    public bool StoppedAtBudget { get; init; }

    /// <summary>Items of this split never sent because the spend guard stopped the run.</summary>
    public int NotSent { get; init; }

    /// <summary>What the endpoint did with the <c>x-tau-raw</c> and <c>x-tau-precision</c> request headers.</summary>
    public required string HeadersNote { get; init; }

    /// <summary>What the latency figures include.</summary>
    public string LatencyNote { get; init; } = "Wall-clock latency per call, network included: the time from sending the request to reading the whole response.";

    /// <summary>Plain-English statement of the spend.</summary>
    public required string SpendNote { get; init; }

    /// <summary>The estimated cost of one priced decision, USD, or null when nothing was priced.</summary>
    public double? UsdPerDecision => PricedResponses == 0 ? null : EstimatedSpendUsd / PricedResponses;
}
