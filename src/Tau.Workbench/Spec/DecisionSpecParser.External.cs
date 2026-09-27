using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace Tau.Workbench.Spec;

/// <content>The optional <c>external:</c> list of hosted endpoints.</content>
internal static partial class DecisionSpecParser
{
    /// <summary>The most requests a hosted endpoint may have in flight.</summary>
    public const int MaxExternalConcurrency = 32;

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex EnvName();

    private static IReadOnlyList<ExternalModelSpec> ParseExternal(
        YamlMappingNode root, Reader r, IReadOnlyList<string> models, IReadOnlyList<string> baselines, List<string> problems)
    {
        if (!root.Children.TryGetValue(new YamlScalarNode("external"), out var node))
        {
            return [];
        }

        if (node is not YamlSequenceNode seq)
        {
            problems.Add("'external' must be a list of hosted endpoints.");
            return [];
        }

        var result = new List<ExternalModelSpec>();
        int index = 0;
        foreach (var child in seq.Children)
        {
            string at = $"external[{index++}].";
            if (child is not YamlMappingNode map)
            {
                problems.Add($"'{at[..^1]}' must be a mapping.");
                continue;
            }

            r.OnlyKeys(map, at, "id", "endpoint", "model", "api_key_env", "price_usd_per_mtok", "budget_usd", "concurrency");
            string id = r.Str(map, "id", at) ?? "";
            string model = r.Str(map, "model", at) ?? "";
            string keyEnv = r.Str(map, "api_key_env", at) ?? "";
            string endpointText = r.Str(map, "endpoint", at) ?? "";
            if (!Uri.TryCreate(endpointText, UriKind.Absolute, out var endpoint) || (endpoint.Scheme != "http" && endpoint.Scheme != "https"))
            {
                problems.Add($"{at}endpoint '{endpointText}' must be an absolute http(s) URL.");
                endpoint = null;
            }
            else if (!string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Query))
            {
                problems.Add($"{at}endpoint must not carry credentials or a query string: the API key is read from api_key_env.");
            }

            if (string.IsNullOrWhiteSpace(model))
            {
                problems.Add($"{at}model must name the model to request (for example jev-latest).");
            }

            if (!EnvName().IsMatch(keyEnv))
            {
                problems.Add($"{at}api_key_env must be the name of an environment variable (letters, digits and '_'), not the key itself.");
            }

            double inPrice = 0, outPrice = 0;
            if (r.Map(map, "price_usd_per_mtok", at) is { } price)
            {
                r.OnlyKeys(price, at + "price_usd_per_mtok.", "input", "output");
                inPrice = r.Num(price, "input", at + "price_usd_per_mtok.") ?? 0;
                outPrice = r.Num(price, "output", at + "price_usd_per_mtok.") ?? 0;
                if (inPrice < 0 || outPrice < 0)
                {
                    problems.Add($"{at}price_usd_per_mtok input and output must not be negative.");
                }
            }

            double budget = r.Num(map, "budget_usd", at) ?? 0;
            if (budget <= 0)
            {
                problems.Add($"{at}budget_usd must be a positive number of US dollars: it is the hard stop on spend.");
            }

            double concurrency = r.Num(map, "concurrency", at, required: false) ?? 4;
            if (concurrency < 1 || concurrency > MaxExternalConcurrency || concurrency != Math.Floor(concurrency))
            {
                problems.Add($"{at}concurrency must be a whole number from 1 to {MaxExternalConcurrency}.");
            }

            result.Add(new ExternalModelSpec(id, endpoint ?? new Uri("http://invalid/"), model, keyEnv, inPrice, outPrice, budget, (int)Math.Clamp(concurrency, 1, MaxExternalConcurrency)));
        }

        var ids = result.Select(e => e.Id).ToArray();
        CheckNames(ids, "external", problems);
        foreach (var clash in ids.Where(id => models.Concat(baselines).Contains(id, StringComparer.OrdinalIgnoreCase)))
        {
            problems.Add($"external id '{clash}' is also a model or baseline name; each needs its own run folder.");
        }

        return result;
    }
}
