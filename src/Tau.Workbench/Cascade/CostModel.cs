using Tau.Workbench.Frontier;
using Tau.Workbench.Spec;

namespace Tau.Workbench.Cascade;

/// <summary>One priced row of the cost table. Every money figure is an estimate.</summary>
public sealed record CostRow
{
    /// <summary>The price row name.</summary>
    public required string Name { get; init; }

    /// <summary>headline, same-model-rate, or what-if.</summary>
    public required string Kind { get; init; }

    /// <summary>A plain-English label for the row.</summary>
    public required string Label { get; init; }

    /// <summary>Input list price, USD per million tokens.</summary>
    public required double InputUsdPerMTok { get; init; }

    /// <summary>Output list price, USD per million tokens.</summary>
    public required double OutputUsdPerMTok { get; init; }

    /// <summary>Estimated frontier cost of one decision, USD.</summary>
    public required double FrontierUsdPerDecision { get; init; }

    /// <summary>Frontier-only, per 1,000 decisions, USD.</summary>
    public required double FrontierOnlyUsdPer1k { get; init; }

    /// <summary>Frontier-only, per million decisions, USD.</summary>
    public required double FrontierOnlyUsdPerMillion { get; init; }

    /// <summary>Frontier-only, per 1,000 decisions, GBP (null when refused).</summary>
    public double? FrontierOnlyGbpPer1k { get; init; }

    /// <summary>Frontier-only, per million decisions, GBP (null when refused).</summary>
    public double? FrontierOnlyGbpPerMillion { get; init; }

    /// <summary>Cascade, per 1,000 decisions, GBP (null when refused or no cascade).</summary>
    public double? CascadeGbpPer1k { get; init; }

    /// <summary>Cascade, per million decisions, GBP (null when refused or no cascade).</summary>
    public double? CascadeGbpPerMillion { get; init; }

    /// <summary>Frontier-only minus cascade, per million decisions, GBP.</summary>
    public double? SavingGbpPerMillion { get; init; }
}

/// <summary>The cost estimate for one cascade, with its whole basis stated.</summary>
public sealed record CostEstimate
{
    /// <summary>Always true: every figure here is an estimate, never a measured bill.</summary>
    public bool Estimate { get; init; } = true;

    /// <summary>Mean estimated input tokens per frontier decision.</summary>
    public required double InputTokensPerDecision { get; init; }

    /// <summary>Mean estimated output tokens per frontier decision.</summary>
    public required double OutputTokensPerDecision { get; init; }

    /// <summary>How many cached answers the token estimate is averaged over.</summary>
    public required int TokenBasisAnswers { get; init; }

    /// <summary>Mean GPU power used for the local estimate, watts.</summary>
    public double? GpuMeanWatts { get; init; }

    /// <summary>Seconds of GPU time per local decision (phase duration / items).</summary>
    public double? SecondsPerDecision { get; init; }

    /// <summary>Estimated local energy per decision, kWh.</summary>
    public double? LocalKwhPerDecision { get; init; }

    /// <summary>Estimated local cost per decision, GBP.</summary>
    public double? LocalGbpPerDecision { get; init; }

    /// <summary>The share of decisions escalated, used for the cascade rows.</summary>
    public double? ShareEscalated { get; init; }

    /// <summary>The priced rows, headline first.</summary>
    public required IReadOnlyList<CostRow> Rows { get; init; }

    /// <summary>Why no pound figure is given at all, when that is the case.</summary>
    public string? GbpRefused { get; init; }

    /// <summary>Why the cascade has no pound figure, when that is the case.</summary>
    public string? CascadeGbpRefused { get; init; }

    /// <summary>Every assumption behind the numbers, in plain English.</summary>
    public required IReadOnlyList<string> Basis { get; init; }
}

/// <summary>
/// The cost model (T027, research R-05/R-06). Frontier tokens are estimated as characters / 4 × the
/// tokenizer factor, from the cached answers' character tallies, and priced at list price per row. Local
/// cost is mean whole-GPU power × seconds per decision, in kWh, at the stated electricity price; when GPU power
/// is unknown, local energy is left out of the cascade figures and the basis says so. Pounds
/// need both the exchange rate and the electricity price; without either, no pound figure is given.
/// </summary>
public static class CostModel
{
    /// <summary>Estimates costs.</summary>
    /// <param name="pricing">The spec's price basis.</param>
    /// <param name="chars">Character tallies of the primary-prompt answers.</param>
    /// <param name="shareEscalated">Share of decisions the cascade sends to the frontier, or null for no cascade.</param>
    /// <param name="gpuMeanWatts">Mean GPU power during the local run, or null when not sampled (local energy is then omitted).</param>
    /// <param name="secondsPerDecision">Local seconds per decision, or null when unknown.</param>
    /// <exception cref="WorkbenchException">No frontier answer is cached, so there is nothing to estimate tokens from.</exception>
    public static CostEstimate Estimate(PricingSpec pricing, CharTally chars, double? shareEscalated, double? gpuMeanWatts, double? secondsPerDecision)
    {
        ArgumentNullException.ThrowIfNull(pricing);
        ArgumentNullException.ThrowIfNull(chars);
        if (chars.Answers == 0)
        {
            throw new WorkbenchException("No frontier answer is cached, so the frontier token count can't be estimated. Run 'tau label' and answer the pending batches first.");
        }

        double inTok = chars.InputChars / (double)chars.Answers / 4.0 * pricing.TokenizerFactor;
        double outTok = chars.OutputChars / (double)chars.Answers / 4.0 * pricing.TokenizerFactor;

        var missing = new List<string>();
        if (pricing.GbpPerUsd is null)
        {
            missing.Add("pricing.gbp_per_usd (the USD to GBP rate)");
        }

        if (pricing.ElectricityGbpPerKwh is null)
        {
            missing.Add("pricing.electricity_gbp_per_kwh (the electricity unit price)");
        }

        string? gbpRefused = missing.Count == 0 ? null : $"No pound figures: the spec is missing {string.Join(" and ", missing)}.";
        double? kwh = gpuMeanWatts is { } w && secondsPerDecision is { } s ? w * s / 3_600_000.0 : null;
        double? localGbp = gbpRefused is null && kwh is { } k ? k * pricing.ElectricityGbpPerKwh!.Value : null;
        // Without GPU power (not sampled, or a model not served through Tau) local energy is left out, not guessed.
        string? cascadeRefused = gbpRefused
            ?? (shareEscalated is null ? "No cascade: no threshold met the target, so there is nothing to price." : null);

        var rows = pricing.Rows
            .OrderBy(r => r.Name == pricing.Headline ? 0 : 1)
            .Select(r =>
            {
                double usd = (inTok * r.InputUsdPerMTok / 1e6) + (outTok * r.OutputUsdPerMTok / 1e6);
                double? gbp = gbpRefused is null ? usd * pricing.GbpPerUsd!.Value : null;
                double? cascade = cascadeRefused is null ? (localGbp ?? 0) + (shareEscalated!.Value * gbp!.Value) : null;
                var (kind, label) = r.Name == pricing.Headline
                    ? ("headline", $"{r.Name} at list price (the model that produced the frontier answers)")
                    : r.Name.StartsWith(pricing.Headline, StringComparison.Ordinal)
                        ? ("same-model-rate", $"{r.Name}: the same model at a different rate")
                        : ("what-if", $"{r.Name}: price only, if escalations went to this model; its accuracy was not measured");
                return new CostRow
                {
                    Name = r.Name,
                    Kind = kind,
                    Label = label,
                    InputUsdPerMTok = r.InputUsdPerMTok,
                    OutputUsdPerMTok = r.OutputUsdPerMTok,
                    FrontierUsdPerDecision = usd,
                    FrontierOnlyUsdPer1k = usd * 1_000,
                    FrontierOnlyUsdPerMillion = usd * 1_000_000,
                    FrontierOnlyGbpPer1k = gbp * 1_000,
                    FrontierOnlyGbpPerMillion = gbp * 1_000_000,
                    CascadeGbpPer1k = cascade * 1_000,
                    CascadeGbpPerMillion = cascade * 1_000_000,
                    SavingGbpPerMillion = cascade is null ? null : (gbp!.Value - cascade.Value) * 1_000_000,
                };
            }).ToArray();

        var basis = new List<string>
        {
            $"Frontier tokens are estimated, not counted: characters / 4 (Anthropic's rule of thumb) × {Fmt.Num(pricing.TokenizerFactor, "0.##")} for the tokenizer, averaged over {Fmt.Int(chars.Answers)} cached answers: {Fmt.Num(inTok, "0.#")} input and {Fmt.Num(outTok, "0.#")} output tokens per decision.",
            $"List prices in US dollars per million tokens, checked {pricing.BasisDate}{(pricing.Source is null ? "" : $" ({pricing.Source})")}.",
            gbpRefused ?? $"USD to GBP at {Fmt.Num(pricing.GbpPerUsd, "0.#####")}{(pricing.GbpPerUsdSource is null ? "" : $" ({pricing.GbpPerUsdSource})")}.",
            "Frontier answers for this benchmark came from an interactive Claude Code session, not the API; the costs price what the same work would cost through the API.",
        };
        if (kwh is not null)
        {
            basis.Add($"Local energy: {Fmt.Num(gpuMeanWatts, "0.#")} W mean whole-GPU power × {Fmt.Num(secondsPerDecision, "0.#####")} s per decision (phase duration / items) = {Fmt.Num(kwh, "0.000e0")} kWh per decision{(pricing.ElectricityGbpPerKwh is { } e ? $", at £{Fmt.Num(e, "0.####")} per kWh{(pricing.ElectricitySource is null ? "" : $" ({pricing.ElectricitySource})")}" : "")}. Hardware purchase and depreciation are excluded.");
        }
        else if (shareEscalated is not null)
        {
            basis.Add("Local energy is not included: no GPU power was measured for the local model, so the cascade figures price the frontier calls only.");
        }

        if (shareEscalated is not null)
        {
            basis.Add($"Cascade: every decision runs locally, and {Fmt.Pct(shareEscalated)} are also sent to the frontier model.");
        }

        return new CostEstimate
        {
            InputTokensPerDecision = inTok,
            OutputTokensPerDecision = outTok,
            TokenBasisAnswers = chars.Answers,
            GpuMeanWatts = gpuMeanWatts,
            SecondsPerDecision = secondsPerDecision,
            LocalKwhPerDecision = kwh,
            LocalGbpPerDecision = localGbp,
            ShareEscalated = shareEscalated,
            Rows = rows,
            GbpRefused = gbpRefused,
            CascadeGbpRefused = cascadeRefused,
            Basis = basis,
        };
    }
}
