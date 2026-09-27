using System.Globalization;
using System.Text.RegularExpressions;
using Tau.Calibration;
using Tau.Workbench.Frontier;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Tau.Workbench.Spec;

/// <summary>
/// Parses <c>decision.yaml</c> with YamlDotNet's representation model (so option order is kept exactly
/// as written) and validates it, collecting every problem rather than stopping at the first.
/// Unknown keys are errors, so a typo never silently falls back to a default.
/// </summary>
internal static partial class DecisionSpecParser
{
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]*$")]
    private static partial Regex SafeName();

    public static DecisionSpec Parse(string yaml, string specPath)
    {
        var problems = new List<string>();
        YamlMappingNode root;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlMappingNode m)
            {
                throw new SpecValidationException(specPath, ["the file must hold exactly one YAML mapping."]);
            }

            root = m;
        }
        catch (YamlException e)
        {
            throw new SpecValidationException(specPath, [$"YAML syntax error at line {e.Start.Line}: {e.Message}"]);
        }

        var r = new Reader(problems);
        r.OnlyKeys(root, "", "name", "title", "question", "data", "endpoint", "models", "baselines", "threshold", "frontier", "pricing");

        string name = r.Str(root, "name", "") ?? "";
        string title = r.Str(root, "title", "", required: false) ?? name;
        if (!SafeName().IsMatch(name))
        {
            problems.Add($"name '{name}' must be a simple identifier (letters, digits, '.', '_' or '-').");
        }

        var question = ParseQuestion(r, r.Map(root, "question", ""), problems);
        var data = ParseData(r, r.Map(root, "data", ""), problems);

        Uri? endpoint = null;
        if (r.Str(root, "endpoint", "", required: false) is { } endpointText)
        {
            if (!Uri.TryCreate(endpointText, UriKind.Absolute, out endpoint) || (endpoint.Scheme != "http" && endpoint.Scheme != "https"))
            {
                problems.Add($"endpoint '{endpointText}' must be an absolute http(s) URL.");
                endpoint = null;
            }
        }

        var models = r.StrList(root, "models", "", required: true);
        if (models.Count == 0)
        {
            problems.Add("models must list at least one model id.");
        }

        CheckNames(models, "models", problems);
        var baselines = r.StrList(root, "baselines", "", required: false);
        CheckNames(baselines, "baselines", problems);

        var thresholdNode = r.Map(root, "threshold", "");
        double targetError = 0.05;
        if (thresholdNode is not null)
        {
            r.OnlyKeys(thresholdNode, "threshold.", "target_error");
            targetError = r.Num(thresholdNode, "target_error", "threshold.") ?? 0.05;
            if (targetError is <= 0 or >= 1)
            {
                problems.Add($"threshold.target_error must be between 0 and 1 (exclusive); got {Fmt(targetError)}.");
            }
        }

        var frontier = ParseFrontier(r, r.Map(root, "frontier", ""), problems);
        var pricing = ParsePricing(r, r.Map(root, "pricing", ""), frontier, problems);

        var repoRoot = DecisionSpec.FindRepoRoot(Path.GetDirectoryName(specPath)!);
        if (repoRoot is null)
        {
            problems.Add("could not find the repository root (a parent directory holding Tau.slnx or .git), which data.dataset is resolved against.");
        }

        if (problems.Count > 0)
        {
            throw new SpecValidationException(specPath, problems);
        }

        return new DecisionSpec(name, title, question!,data!, endpoint, models, baselines,
            new ThresholdSpec(targetError), frontier!, pricing!, specPath, repoRoot!);
    }

    private static QuestionSpec? ParseQuestion(Reader r, YamlMappingNode? node, List<string> problems)
    {
        if (node is null)
        {
            return null;
        }

        r.OnlyKeys(node, "question.", "key", "type", "instructions", "options");
        string key = r.Str(node, "key", "question.") ?? "";
        if (key.Length == 0)
        {
            problems.Add("question.key must not be empty.");
        }

        string typeText = r.Str(node, "type", "question.") ?? "";
        QuestionType? type = QuestionTypeExtensions.FromWireString(typeText);
        if (type is null)
        {
            problems.Add($"question.type '{typeText}' must be choice, score or noul.");
        }

        string instructions = r.Str(node, "instructions", "question.") ?? "";
        if (string.IsNullOrWhiteSpace(instructions))
        {
            problems.Add("question.instructions must not be empty.");
        }

        var options = new List<SpecOption>();
        node.Children.TryGetValue(new YamlScalarNode("options"), out var optionsNode);
        switch (type)
        {
            case QuestionType.Choice:
                if (optionsNode is not YamlMappingNode choiceMap)
                {
                    problems.Add("question.options must be a mapping of option key to description for a choice question.");
                    break;
                }

                foreach (var (k, v) in choiceMap.Children)
                {
                    options.Add(new SpecOption(((YamlScalarNode)k).Value ?? "", (v as YamlScalarNode)?.Value ?? ""));
                }

                if (options.Count is < 2 or > 255)
                {
                    problems.Add($"a choice question needs 2 to 255 options; got {options.Count}.");
                }

                foreach (var o in options.Where(o => string.IsNullOrWhiteSpace(o.Key) || o.Key != o.Key.Trim()))
                {
                    problems.Add($"option key '{o.Key}' must be non-empty with no surrounding spaces.");
                }

                break;
            case QuestionType.Score:
                if (optionsNode is not YamlSequenceNode levels)
                {
                    problems.Add("question.options must be an ordered list of levels (lowest first) for a score question.");
                    break;
                }

                int i = 0;
                foreach (var level in levels.Children)
                {
                    options.Add(new SpecOption((i++).ToString(CultureInfo.InvariantCulture), (level as YamlScalarNode)?.Value ?? ""));
                }

                if (options.Count is < 2 or > 10)
                {
                    problems.Add($"a score question needs 2 to 10 levels; got {options.Count}.");
                }

                foreach (var o in options.Where(o => string.IsNullOrWhiteSpace(o.Description)))
                {
                    problems.Add($"score level {o.Key} has no description.");
                }

                break;
            case QuestionType.Noul:
                if (optionsNode is null)
                {
                    break;
                }

                if (optionsNode is not YamlMappingNode noulMap)
                {
                    problems.Add("question.options for a noul question must be a mapping with keys true and/or false.");
                    break;
                }

                foreach (var (k, v) in noulMap.Children)
                {
                    var outcome = (((YamlScalarNode)k).Value ?? "").ToLowerInvariant();
                    if (outcome is not ("true" or "false"))
                    {
                        problems.Add($"noul option key '{outcome}' must be true or false.");
                        continue;
                    }

                    options.Add(new SpecOption(outcome, (v as YamlScalarNode)?.Value ?? ""));
                }

                break;
        }

        var duplicates = options.GroupBy(o => o.Key, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        foreach (var d in duplicates)
        {
            problems.Add($"option key '{d}' appears more than once.");
        }

        return type is null ? null : new QuestionSpec(key, type.Value, instructions, options);
    }

    private static DataSpec? ParseData(Reader r, YamlMappingNode? node, List<string> problems)
    {
        if (node is null)
        {
            return null;
        }

        r.OnlyKeys(node, "data.", "dataset", "text_field", "label_field");
        string dataset = r.Str(node, "dataset", "data.") ?? "";
        if (!SafeName().IsMatch(dataset) || dataset.Contains("..", StringComparison.Ordinal))
        {
            problems.Add($"data.dataset '{dataset}' must be a simple directory name under data/.");
        }

        return new DataSpec(
            dataset,
            r.Str(node, "text_field", "data.", required: false) ?? "text",
            r.Str(node, "label_field", "data.", required: false) ?? "label");
    }

    private static FrontierSpec? ParseFrontier(Reader r, YamlMappingNode? node, List<string> problems)
    {
        if (node is null)
        {
            return null;
        }

        r.OnlyKeys(node, "frontier.", "model", "prompt_version", "alt_prompt_version", "alt_subset");
        string model = r.Str(node, "model", "frontier.") ?? "";
        string pv = r.Str(node, "prompt_version", "frontier.", required: false) ?? PromptTemplates.Primary;
        string alt = r.Str(node, "alt_prompt_version", "frontier.", required: false) ?? PromptTemplates.Alternative;
        double altSubset = r.Num(node, "alt_subset", "frontier.", required: false) ?? 200;
        foreach (var v in new[] { pv, alt }.Where(v => !PromptTemplates.Known.Contains(v)))
        {
            problems.Add($"frontier prompt version '{v}' is unknown; known versions: {string.Join(", ", PromptTemplates.Known)}.");
        }

        if (pv == alt)
        {
            problems.Add("frontier.prompt_version and frontier.alt_prompt_version must differ, or the agreement check measures nothing.");
        }

        if (altSubset < 0 || altSubset > FrontierLimits.MaxHeldOutItems || altSubset != Math.Floor(altSubset))
        {
            problems.Add($"frontier.alt_subset must be a whole number from 0 to {FrontierLimits.MaxHeldOutItems}.");
        }

        return new FrontierSpec(model, pv, alt, (int)altSubset);
    }

    private static PricingSpec? ParsePricing(Reader r, YamlMappingNode? node, FrontierSpec? frontier, List<string> problems)
    {
        if (node is null)
        {
            return null;
        }

        r.OnlyKeys(node, "pricing.", "basis_date", "source", "usd_per_mtok", "headline", "gbp_per_usd", "gbp_per_usd_source",
            "electricity_gbp_per_kwh", "electricity_source", "tokenizer_factor");
        string basisDate = r.Str(node, "basis_date", "pricing.") ?? "";
        if (!DateOnly.TryParseExact(basisDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            problems.Add($"pricing.basis_date '{basisDate}' must be a date written yyyy-MM-dd.");
        }

        var rows = new List<PriceRow>();
        if (r.Map(node, "usd_per_mtok", "pricing.") is { } priceMap)
        {
            foreach (var (k, v) in priceMap.Children)
            {
                string rowName = ((YamlScalarNode)k).Value ?? "";
                if (v is not YamlSequenceNode { Children.Count: 2 } pair
                    || !TryNum(pair.Children[0], out double inPrice) || !TryNum(pair.Children[1], out double outPrice)
                    || inPrice < 0 || outPrice < 0)
                {
                    problems.Add($"pricing.usd_per_mtok.{rowName} must be [input, output] non-negative US dollars per million tokens.");
                    continue;
                }

                rows.Add(new PriceRow(rowName, inPrice, outPrice));
            }
        }

        string headline = r.Str(node, "headline", "pricing.", required: false) ?? frontier?.Model ?? "";
        if (rows.All(p => p.Name != headline))
        {
            problems.Add($"pricing.usd_per_mtok needs a row for the headline model '{headline}' (pricing.headline, or frontier.model when that is not set).");
        }

        double? gbp = r.Num(node, "gbp_per_usd", "pricing.", required: false);
        double? kwh = r.Num(node, "electricity_gbp_per_kwh", "pricing.", required: false);
        double factor = r.Num(node, "tokenizer_factor", "pricing.", required: false) ?? 1.0;
        if (gbp < 0 || kwh < 0)
        {
            problems.Add("pricing.gbp_per_usd and pricing.electricity_gbp_per_kwh must not be negative (0 means unknown).");
        }

        if (factor <= 0)
        {
            problems.Add("pricing.tokenizer_factor must be positive.");
        }

        return new PricingSpec(
            basisDate,
            r.Str(node, "source", "pricing.", required: false),
            rows,
            headline,
            gbp is > 0 ? gbp : null,
            r.Str(node, "gbp_per_usd_source", "pricing.", required: false),
            kwh is > 0 ? kwh : null,
            r.Str(node, "electricity_source", "pricing.", required: false),
            factor);
    }

    private static void CheckNames(IReadOnlyList<string> names, string field, List<string> problems)
    {
        foreach (var n in names.Where(n => !SafeName().IsMatch(n) || n.Contains("..", StringComparison.Ordinal)))
        {
            problems.Add($"{field} entry '{n}' must be a simple name (letters, digits, '.', '_' or '-'); it becomes a directory or file name.");
        }

        foreach (var d in names.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            problems.Add($"{field} lists '{d.Key}' more than once.");
        }
    }

    private static bool TryNum(YamlNode node, out double value)
    {
        value = 0;
        return node is YamlScalarNode s && double.TryParse(s.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string Fmt(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>Typed accessors that record a problem instead of throwing.</summary>
    private sealed class Reader(List<string> problems)
    {
        public void OnlyKeys(YamlMappingNode node, string prefix, params string[] allowed)
        {
            foreach (var key in node.Children.Keys.OfType<YamlScalarNode>().Select(k => k.Value ?? "").Where(k => !allowed.Contains(k)))
            {
                problems.Add($"unknown key '{prefix}{key}' (allowed here: {string.Join(", ", allowed)}).");
            }
        }

        public YamlMappingNode? Map(YamlMappingNode node, string key, string prefix)
        {
            if (!node.Children.TryGetValue(new YamlScalarNode(key), out var value))
            {
                problems.Add($"missing required section '{prefix}{key}'.");
                return null;
            }

            if (value is not YamlMappingNode map)
            {
                problems.Add($"'{prefix}{key}' must be a mapping.");
                return null;
            }

            return map;
        }

        public string? Str(YamlMappingNode node, string key, string prefix, bool required = true)
        {
            if (!node.Children.TryGetValue(new YamlScalarNode(key), out var value))
            {
                if (required)
                {
                    problems.Add($"missing required key '{prefix}{key}'.");
                }

                return null;
            }

            if (value is not YamlScalarNode scalar)
            {
                problems.Add($"'{prefix}{key}' must be a single value.");
                return null;
            }

            return scalar.Value;
        }

        public double? Num(YamlMappingNode node, string key, string prefix, bool required = true)
        {
            var text = Str(node, key, prefix, required);
            if (text is null)
            {
                return null;
            }

            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || !double.IsFinite(v))
            {
                problems.Add($"'{prefix}{key}' must be a number; got '{text}'.");
                return null;
            }

            return v;
        }

        public IReadOnlyList<string> StrList(YamlMappingNode node, string key, string prefix, bool required)
        {
            if (!node.Children.TryGetValue(new YamlScalarNode(key), out var value))
            {
                if (required)
                {
                    problems.Add($"missing required key '{prefix}{key}'.");
                }

                return [];
            }

            if (value is not YamlSequenceNode seq)
            {
                problems.Add($"'{prefix}{key}' must be a list.");
                return [];
            }

            return seq.Children.Select(c => (c as YamlScalarNode)?.Value ?? "").ToArray();
        }
    }
}
