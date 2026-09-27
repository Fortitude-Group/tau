using Tau.Calibration;

namespace Tau.Workbench.Spec;

/// <summary>One option of a choice question, one level of a score question, or one outcome of a noul question.</summary>
/// <param name="Key">The option key (choice), the level index "0".."k-1" (score), or "true"/"false" (noul).</param>
/// <param name="Description">The text shown to the model and to the frontier answerer.</param>
public sealed record SpecOption(string Key, string Description);

/// <summary>The single question a decision spec asks.</summary>
/// <param name="Key">The question's name in the <c>/v1/systemone</c> request.</param>
/// <param name="Type">choice, score or noul.</param>
/// <param name="Instructions">The instructions sent to the model and used in the frontier prompt.</param>
/// <param name="Options">
/// Choice: every option key with its description, in declared order. Score: every level in order, keyed
/// "0".."k-1". Noul: the optional true/false descriptions (keys "true", "false").
/// </param>
public sealed record QuestionSpec(string Key, QuestionType Type, string Instructions, IReadOnlyList<SpecOption> Options)
{
    /// <summary>
    /// The class labels in probability-vector order: choice option keys in declared order; score levels
    /// "0".."k-1"; noul <c>["false", "true"]</c> (so a noul probability p becomes <c>[1 - p, p]</c>).
    /// </summary>
    public IReadOnlyList<string> Classes => Type switch
    {
        QuestionType.Noul => ["false", "true"],
        _ => Options.Select(o => o.Key).ToArray(),
    };

    /// <summary>The answers a frontier reply may contain: option keys, level indices, or "true"/"false".</summary>
    public IReadOnlyList<string> AllowedAnswers => Type switch
    {
        QuestionType.Noul => ["true", "false"],
        _ => Options.Select(o => o.Key).ToArray(),
    };

    /// <summary>
    /// Maps a gold or frontier label to its index in <see cref="Classes"/>, or null when it is not a class.
    /// Labels are compared exactly (ordinal), except noul, which accepts true/false in any letter case.
    /// </summary>
    /// <param name="label">The label.</param>
    public int? ClassIndex(string? label)
    {
        if (label is null)
        {
            return null;
        }

        if (Type == QuestionType.Noul)
        {
            return label.Trim().ToLowerInvariant() switch
            {
                "false" => 0,
                "true" => 1,
                _ => null,
            };
        }

        for (int i = 0; i < Options.Count; i++)
        {
            if (string.Equals(Options[i].Key, label, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return null;
    }

    /// <summary>The description for a class label, or the label itself when there is none.</summary>
    /// <param name="label">A class label.</param>
    public string Describe(string label) =>
        Options.FirstOrDefault(o => string.Equals(o.Key, label, StringComparison.OrdinalIgnoreCase))?.Description is { Length: > 0 } d
            ? d
            : label;
}

/// <summary>What every score is measured against (<c>data.reference</c> in the spec).</summary>
public enum ReferenceKind
{
    /// <summary>The dataset's own gold labels (the default).</summary>
    Gold,

    /// <summary>
    /// The frontier model's cached primary-prompt answers, after human overrides. The question becomes
    /// "can a local model stand in for the frontier call", and every rate is agreement with the frontier model.
    /// </summary>
    Frontier,
}

/// <summary>Where the prepared data lives, which fields hold the text and gold label, and what scores are measured against.</summary>
/// <param name="Dataset">The prepared dataset name: files live at <c>&lt;repo&gt;/data/&lt;dataset&gt;/</c>.</param>
/// <param name="TextField">The JSONL field holding the state text.</param>
/// <param name="LabelField">The JSONL field holding the gold label.</param>
/// <param name="Reference">The reference labels: the dataset's gold labels, or the frontier model's answers.</param>
public sealed record DataSpec(string Dataset, string TextField, string LabelField, ReferenceKind Reference = ReferenceKind.Gold);

/// <summary>The threshold stage's target.</summary>
/// <param name="TargetError">The largest acceptable error rate on locally accepted items, in (0, 1).</param>
public sealed record ThresholdSpec(double TargetError);

/// <summary>Frontier labelling settings.</summary>
/// <param name="Model">The frontier model id recorded in provenance, and the headline pricing row.</param>
/// <param name="PromptVersion">The primary prompt version (all held-out items).</param>
/// <param name="AltPromptVersion">The alternative wording (a subset), for the agreement check.</param>
/// <param name="AltSubset">How many held-out items get the alternative wording.</param>
public sealed record FrontierSpec(string Model, string PromptVersion, string AltPromptVersion, int AltSubset);

/// <summary>A frontier list price in US dollars per million tokens.</summary>
/// <param name="Name">The row name, for example <c>claude-opus-5-5</c> or <c>claude-opus-5-5-batch</c>.</param>
/// <param name="InputUsdPerMTok">Input price.</param>
/// <param name="OutputUsdPerMTok">Output price.</param>
public sealed record PriceRow(string Name, double InputUsdPerMTok, double OutputUsdPerMTok);

/// <summary>The price basis for the cost estimates.</summary>
/// <param name="BasisDate">The date the prices were checked (yyyy-MM-dd).</param>
/// <param name="Source">Where the list prices came from (optional free text).</param>
/// <param name="Rows">Price rows, in declared order.</param>
/// <param name="Headline">The headline row: the model that produced the frontier answers.</param>
/// <param name="GbpPerUsd">USD to GBP rate; null or 0 means unknown, and no pound figure is printed.</param>
/// <param name="GbpPerUsdSource">Where the exchange rate came from (optional free text).</param>
/// <param name="ElectricityGbpPerKwh">UK electricity unit price; null or 0 means unknown.</param>
/// <param name="ElectricitySource">Where the electricity price came from (optional free text).</param>
/// <param name="TokenizerFactor">Multiplier on chars/4 for the frontier tokenizer.</param>
public sealed record PricingSpec(
    string BasisDate,
    string? Source,
    IReadOnlyList<PriceRow> Rows,
    string Headline,
    double? GbpPerUsd,
    string? GbpPerUsdSource,
    double? ElectricityGbpPerKwh,
    string? ElectricitySource,
    double TokenizerFactor);
