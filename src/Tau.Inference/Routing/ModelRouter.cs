using System.Globalization;
using System.Text.Json.Nodes;

namespace Tau.Inference.Routing;

/// <summary>Which model answers a request, and why.</summary>
/// <param name="ModelId">The resolved Tau model id, e.g. <c>laya-en</c>.</param>
/// <param name="Reason">A short explanation in the reference router's wording.</param>
/// <param name="Auto">True when the model was chosen by detection rather than named by the caller.</param>
public sealed record RouteDecision(string ModelId, string Reason, bool Auto);

/// <summary>The requested model is neither an installed Tau id nor an auto-route alias.</summary>
public sealed class UnknownModelException : Exception
{
    /// <summary>Create the exception for <paramref name="requested"/>.</summary>
    /// <param name="requested">The model string the caller sent.</param>
    /// <param name="installed">The installed Tau model ids.</param>
    /// <param name="aliases">The auto-route aliases (plus the <c>jev-*</c> pattern).</param>
    public UnknownModelException(string requested, IEnumerable<string> installed, IEnumerable<string> aliases)
        : base(string.Create(CultureInfo.InvariantCulture,
            $"unknown model '{requested}'; choose one of [{string.Join(", ", installed.Order(StringComparer.Ordinal))}] or an alias: [{string.Join(", ", aliases)}]"))
    {
        Requested = requested;
        Installed = installed.Order(StringComparer.Ordinal).ToArray();
        Aliases = aliases.ToArray();
    }

    /// <summary>The model string the caller sent.</summary>
    public string Requested { get; }

    /// <summary>The installed Tau model ids, sorted.</summary>
    public IReadOnlyList<string> Installed { get; }

    /// <summary>The auto-route aliases the router accepts.</summary>
    public IReadOnlyList<string> Aliases { get; }
}

/// <summary>
/// Resolves a request's <c>model</c> to a Tau model id: an installed id is used as named; an auto-route
/// alias runs the detection branch of <c>laya.router.Router._route</c> (default English, no task
/// detection, no language hint) and picks <c>laya-en</c> or <c>laya-multilingual</c>.
/// <c>laya-typed-decisions</c> is never chosen automatically.
/// </summary>
public sealed class ModelRouter
{
    /// <summary>The English checkpoint's Tau id.</summary>
    public const string LayaEnglish = "laya-en";

    /// <summary>The multilingual checkpoint's Tau id.</summary>
    public const string LayaMultilingual = "laya-multilingual";

    /// <summary>The typed-decisions checkpoint's Tau id (explicit only).</summary>
    public const string LayaTypedDecisions = "laya-typed-decisions";

    /// <summary>The Von model's Tau id.</summary>
    public const string Von = "von-1.2.0";

    /// <summary>The default auto-route aliases.</summary>
    public static readonly IReadOnlyList<string> DefaultAliases = ["auto", "tau-auto", "jev-latest"];

    private const string JevPrefix = "jev-";

    private readonly HashSet<string> _aliases;
    private readonly IReadOnlyList<string> _aliasList;

    /// <summary>Create a router with the given auto-route aliases (the <c>jev-*</c> pattern always applies).</summary>
    /// <param name="aliases">Aliases that mean "choose for me"; null uses <see cref="DefaultAliases"/>.</param>
    public ModelRouter(IEnumerable<string>? aliases = null)
    {
        _aliasList = (aliases ?? DefaultAliases).Select(a => a.Trim().ToLowerInvariant()).Distinct().ToArray();
        _aliases = new HashSet<string>(_aliasList, StringComparer.Ordinal);
    }

    /// <summary>Resolve <paramref name="model"/> for a request whose state is <paramref name="state"/>.</summary>
    /// <param name="model">The requested model id or alias; null is treated as an auto-route request.</param>
    /// <param name="state">The request state, analysed only when routing automatically.</param>
    /// <param name="installed">The installed Tau model ids.</param>
    /// <returns>The decision.</returns>
    /// <exception cref="UnknownModelException">The model is neither installed nor an alias.</exception>
    /// <exception cref="InvalidOperationException">Detection chose a checkpoint that is not installed.</exception>
    public RouteDecision Resolve(string? model, JsonNode? state, IReadOnlySet<string> installed)
    {
        ArgumentNullException.ThrowIfNull(installed);
        var key = model?.Trim().ToLowerInvariant();
        if (key is not null && !IsAlias(key))
        {
            if (installed.Contains(key))
            {
                return new RouteDecision(key, $"explicit model='{key}'", Auto: false);
            }

            throw new UnknownModelException(model!, installed, [.. _aliasList, JevPrefix + "*"]);
        }

        var decision = Route(ScriptAnalyser.Analyse(state));
        if (!installed.Contains(decision.ModelId))
        {
            throw new InvalidOperationException(
                $"auto-routing chose '{decision.ModelId}' ({decision.Reason}), but it is not installed");
        }

        return decision;
    }

    /// <summary>The detection branch of <c>Router._route</c> with the stock <c>default="english"</c>.</summary>
    /// <param name="det">The analysis of the state.</param>
    /// <returns>An auto decision for <c>laya-en</c> or <c>laya-multilingual</c>.</returns>
    public static RouteDecision Route(ScriptAnalysis det)
    {
        ArgumentNullException.ThrowIfNull(det);
        const string defaultKey = "english";
        if (det.Script == "unknown")
        {
            return new(LayaEnglish, $"no letters detected in state; using default ({defaultKey})", true);
        }

        if (det.Script != "latin")
        {
            return new(LayaMultilingual, string.Create(CultureInfo.InvariantCulture,
                $"non-Latin script ({det.Script}, {Percent(det.NonLatinFraction)}% of letters); the English checkpoint cannot read it"), true);
        }

        if (!det.IsEnglish)
        {
            var reason = det.Language is not null
                ? $"Latin script but language looks like '{det.Language}', not English"
                : string.Create(CultureInfo.InvariantCulture,
                    $"Latin script, language not identified but {Percent(det.DiacriticRate)}% non-English letters; not safe for the English checkpoint");
            return new(LayaMultilingual, reason, true);
        }

        if (det.LanguageUndecided)
        {
            return new(LayaEnglish, $"Latin script, language not identified and no non-English letters; using default ({defaultKey})", true);
        }

        return new(LayaEnglish, "English Latin text", true);
    }

    private bool IsAlias(string key) => _aliases.Contains(key) || key.StartsWith(JevPrefix, StringComparison.Ordinal);

    /// <summary>Python's <c>"%.0f" % (100 * x)</c>: round half to even on the exact double.</summary>
    private static string Percent(double fraction) =>
        Math.Round(100 * fraction, MidpointRounding.ToEven).ToString("F0", CultureInfo.InvariantCulture);
}
