namespace Tau.Inference.Routing;

/// <summary>
/// The result of <see cref="ScriptAnalyser.Analyse"/>, field for field the dict that
/// <c>laya.lang.analyse</c> returns.
/// </summary>
/// <param name="Script">
/// Dominant script: <c>latin</c>, a named script such as <c>han</c> or <c>cyrillic</c>, <c>other</c> for
/// letters no named range claims, or <c>unknown</c> when the state holds no letters.
/// </param>
/// <param name="ScriptProfile">
/// Share of alphabetic characters per script, in the reference's dict order (<c>latin</c> first, then
/// scripts in the order first seen); scripts with no letters are left out.
/// </param>
/// <param name="Language">Best-effort language code for Latin text (<c>en</c>, <c>fr</c>, ...), or null when undecided.</param>
/// <param name="IsEnglish">True when the English checkpoint can be expected to read the state.</param>
/// <param name="LanguageUndecided">True when nothing identified the language.</param>
/// <param name="DiacriticRate">Share of lower-cased characters that ordinary English does not use, rounded to 4 places.</param>
/// <param name="NonLatinFraction">Share of letters outside the Latin script, rounded to 4 places.</param>
public sealed record ScriptAnalysis(
    string Script,
    IReadOnlyList<KeyValuePair<string, double>> ScriptProfile,
    string? Language,
    bool IsEnglish,
    bool LanguageUndecided,
    double DiacriticRate,
    double NonLatinFraction);
