using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tau.Inference.Routing;

/// <summary>
/// A faithful port of <c>laya.lang.analyse</c> (laya 0.3.20): script shares, the Latin-script language
/// guess and the English/non-English verdict that routing turns into a checkpoint.
/// </summary>
/// <remarks>
/// Python measures and iterates strings per code point, so everything here works on code points, never
/// on UTF-16 chars. Character classes come from <see cref="PyUnicodeTables"/>, generated from the
/// reference interpreter, so a Unicode-version difference between Python and .NET cannot move a route.
/// </remarks>
public static class ScriptAnalyser
{
    private const int MaxDepth = 6;
    private const int MaxChars = 4000;
    private const int DottedCapitalI = 0x0130;

    /// <summary>Analyse a request state (string, object, array, or null) exactly as <c>laya.lang.analyse</c> does.</summary>
    /// <param name="state">The state. Object keys are ignored; numbers, booleans and nulls contribute nothing.</param>
    /// <returns>The detection result.</returns>
    public static ScriptAnalysis Analyse(JsonNode? state) => AnalyseText(StateText(state));

    /// <summary>Analyse already-flattened text (the output of <c>state_text</c>).</summary>
    internal static ScriptAnalysis AnalyseText(int[] text)
    {
        var (nonLatinOrder, counts, latin) = CountScripts(text);
        var total = latin;
        foreach (var name in nonLatinOrder)
        {
            total += counts[name];
        }

        var profile = Profile(nonLatinOrder, counts, latin, total);
        var script = DetectScript(nonLatinOrder, counts, latin, total);

        var nonLatin = 0.0;
        if (profile.Count > 0)
        {
            var latinShare = 0.0;
            foreach (var kv in profile)
            {
                if (kv.Key == "latin")
                {
                    latinShare = kv.Value;
                }
            }

            nonLatin = PyRound.Round(1.0 - latinShare, 4);
        }

        // `total` is the number of alphabetic code points: each one is counted under exactly one script.
        var nNonLatin = Math.Round(nonLatin * total, MidpointRounding.ToEven);
        if (script == "latin" && HasNonLatinWords(text) && (
                nonLatin >= LayaLangData.NonLatinFraction || (
                    nonLatin >= LayaLangData.NonLatinMinFraction && nNonLatin >= LayaLangData.NonLatinMinLetters)))
        {
            string? best = null;
            var bestShare = 0.0;
            foreach (var kv in profile)
            {
                if (kv.Key != "latin" && (best is null || kv.Value > bestShare))
                {
                    best = kv.Key;
                    bestShare = kv.Value;
                }
            }

            script = best!;
        }

        if (script == "unknown")
        {
            return new ScriptAnalysis("unknown", profile, null, true, true, 0.0, 0.0);
        }

        if (script != "latin")
        {
            return new ScriptAnalysis(script, profile, null, false, true, 0.0, nonLatin);
        }

        var (lang, diacriticRate, looksNonEnglish) = LatinProfile(text);
        var undecided = lang is null;
        var english = lang == "en" || (undecided && !looksNonEnglish);
        return new ScriptAnalysis("latin", profile, lang, english, undecided, PyRound.Round(diacriticRate, 4), nonLatin);
    }

    /// <summary><c>state_text</c>: the string leaves joined by single spaces, capped at 4000 code points.</summary>
    internal static int[] StateText(JsonNode? state)
    {
        var leaves = new List<int[]>();
        IterText(state, 0, leaves);

        var parts = new List<int[]>();
        var budget = MaxChars;
        foreach (var leaf in leaves)
        {
            if (budget <= 0)
            {
                break;
            }

            if (leaf.Length > budget)
            {
                parts.Add(leaf[..budget]);
                break;
            }

            parts.Add(leaf);
            budget -= leaf.Length + 1;
        }

        var joined = new List<int>();
        for (var i = 0; i < parts.Count; i++)
        {
            if (i > 0)
            {
                joined.Add(' ');
            }

            joined.AddRange(parts[i]);
        }

        return joined.Count > MaxChars ? joined.GetRange(0, MaxChars).ToArray() : joined.ToArray();
    }

    /// <summary>Code points of a .NET string (lone surrogates become U+FFFD).</summary>
    internal static int[] CodePointsOf(string s)
    {
        var list = new List<int>(s.Length);
        foreach (var rune in s.EnumerateRunes())
        {
            list.Add(rune.Value);
        }

        return list.ToArray();
    }

    private static void IterText(JsonNode? node, int depth, List<int[]> leaves)
    {
        if (depth > MaxDepth || node is null)
        {
            return;
        }

        switch (node)
        {
            case JsonObject obj:
                foreach (var kv in obj)
                {
                    IterText(kv.Value, depth + 1, leaves);
                }

                break;
            case JsonArray arr:
                foreach (var item in arr)
                {
                    IterText(item, depth + 1, leaves);
                }

                break;
            case JsonValue value when value.GetValueKind() == JsonValueKind.String:
                leaves.Add(CodePointsOf(value.GetValue<string>()));
                break;
            default:
                break; // numbers and booleans contribute nothing
        }
    }

    private static bool IsProfileLatin(int cp) =>
        cp < 0x02B0 || (cp >= 0x1E00 && cp <= 0x1EFF) || (cp >= 0xFF21 && cp <= 0xFF3A) || (cp >= 0xFF41 && cp <= 0xFF5A);

    /// <summary>The named script whose ranges claim <paramref name="cp"/>, first match wins, or null.</summary>
    private static string? NamedScript(int cp)
    {
        foreach (var (name, ranges) in LayaLangData.ScriptRanges)
        {
            foreach (var (lo, hi) in ranges)
            {
                if (lo <= cp && cp <= hi)
                {
                    return name;
                }
            }
        }

        return null;
    }

    /// <summary><c>_script_of</c>: the named non-Latin script of one character, or null.</summary>
    private static string? ScriptOf(int cp)
    {
        if (cp < 0x0250 || (cp >= 0x1E00 && cp <= 0x1EFF) || (cp >= 0xFF21 && cp <= 0xFF3A) || (cp >= 0xFF41 && cp <= 0xFF5A))
        {
            return null;
        }

        return NamedScript(cp);
    }

    private static (List<string> Order, Dictionary<string, int> Counts, int Latin) CountScripts(int[] text)
    {
        var order = new List<string>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var latin = 0;
        foreach (var cp in text)
        {
            if (!PyUnicodeTables.IsAlpha(cp))
            {
                continue;
            }

            if (IsProfileLatin(cp))
            {
                latin++;
                continue;
            }

            var name = NamedScript(cp) ?? "other";
            if (counts.TryGetValue(name, out var n))
            {
                counts[name] = n + 1;
            }
            else
            {
                counts[name] = 1;
                order.Add(name);
            }
        }

        return (order, counts, latin);
    }

    /// <summary><c>detect_script</c>: scripts in first-seen order, then <c>latin</c> last; ties go to the earliest.</summary>
    private static string DetectScript(List<string> order, Dictionary<string, int> counts, int latin, int total)
    {
        if (total == 0)
        {
            return "unknown";
        }

        string? best = null;
        var bestCount = 0;
        foreach (var name in order)
        {
            if (best is null || counts[name] > bestCount)
            {
                best = name;
                bestCount = counts[name];
            }
        }

        if (best is null || latin > bestCount)
        {
            best = "latin";
        }

        return best;
    }

    /// <summary><c>script_profile</c>: <c>latin</c> first, then scripts in first-seen order, zero shares dropped.</summary>
    private static List<KeyValuePair<string, double>> Profile(List<string> order, Dictionary<string, int> counts, int latin, int total)
    {
        var profile = new List<KeyValuePair<string, double>>();
        if (total == 0)
        {
            return profile;
        }

        if (latin > 0)
        {
            profile.Add(new("latin", (double)latin / total));
        }

        foreach (var name in order)
        {
            profile.Add(new(name, (double)counts[name] / total));
        }

        return profile;
    }

    /// <summary><c>_non_latin_words</c>, reduced to "is there at least one".</summary>
    private static bool HasNonLatinWords(int[] text)
    {
        var runs = new List<List<int>>();
        List<int>? cur = null;
        string? script = null;
        foreach (var cp in text)
        {
            if (PyUnicodeTables.IsCombining(cp))
            {
                continue;
            }

            var s = ScriptOf(cp);
            if (s is not null && s == script)
            {
                cur!.Add(cp);
                continue;
            }

            if (cur is { Count: > 0 })
            {
                runs.Add(cur);
            }

            if (s is not null)
            {
                cur = [cp];
                script = s;
            }
            else
            {
                cur = null;
                script = null;
            }
        }

        if (cur is { Count: > 0 })
        {
            runs.Add(cur);
        }

        return runs.Any(w => w.Count >= 2 && !PyUnicodeTables.IsUpper(w[0]));
    }

    /// <summary><c>latin_profile</c>: the language guess, the diacritic rate and the non-English flag.</summary>
    private static (string? Language, double DiacriticRate, bool LooksNonEnglish) LatinProfile(int[] text)
    {
        // words = _WORD.findall(_IDENTIFIER.sub(" ", text).replace("İ", "i").lower())
        var stripped = StripIdentifiers(text);
        for (var i = 0; i < stripped.Count; i++)
        {
            stripped[i] = stripped[i] == DottedCapitalI ? 'i' : PyUnicodeTables.Lower(stripped[i]);
        }

        var words = FindWords(stripped);

        // lowered = text.lower(): U+0130 lowers to two code points ('i' + U+0307), neither a diacritic here.
        var loweredLength = 0;
        var diac = 0;
        foreach (var cp in text)
        {
            if (cp == DottedCapitalI)
            {
                loweredLength += 2;
                continue;
            }

            loweredLength++;
            if (LayaLangData.NonEnDiacritics.Contains(PyUnicodeTables.Lower(cp)))
            {
                diac++;
            }
        }

        var diacRate = (double)diac / Math.Max(1, loweredLength);
        var nonEnglish = diacRate >= LayaLangData.NonEnDiacriticRate;
        if (words.Count < 4)
        {
            return (null, diacRate, nonEnglish);
        }

        var wordSet = new HashSet<string>(words, StringComparer.Ordinal);
        var en = 0;
        string? bestLang = null;
        var best = 0;
        foreach (var (lang, stop) in LayaLangData.Stop)
        {
            var score = 0;
            foreach (var w in words)
            {
                if (stop.Contains(w))
                {
                    score++;
                }
            }

            if (lang == "en")
            {
                en = score;
                continue;
            }

            var evidenced = false;
            foreach (var w in wordSet)
            {
                if (stop.Contains(w) && !LayaLangData.SharedWords.Contains(w))
                {
                    evidenced = true;
                    break;
                }
            }

            if (evidenced && (bestLang is null || score > best))
            {
                bestLang = lang;
                best = score;
            }
        }

        string? result = null;
        if (bestLang is not null && best >= Math.Max(2, en + 2))
        {
            result = bestLang;
        }
        else if (bestLang is not null && nonEnglish && best >= Math.Max(2, en))
        {
            result = bestLang;
        }
        else if (en > 0 && !nonEnglish)
        {
            result = "en";
        }

        return (result, diacRate, nonEnglish);
    }

    private static bool IsIdentChar(int cp) => cp == '-' || PyUnicodeTables.IsWord(cp);

    /// <summary>
    /// <c>re.sub(r"[\w-]*(?:[.@][\w-]+)+", " ", text)</c>. At each start the greedy prefix run can only be
    /// followed by a separator if it is the whole run, so a match exists exactly when the maximal
    /// <c>[\w-]</c> run is followed by <c>[.@]</c> and another <c>[\w-]</c>; the repetition then takes
    /// every further <c>[.@][\w-]+</c> step.
    /// </summary>
    private static List<int> StripIdentifiers(int[] text)
    {
        var output = new List<int>(text.Length);
        var n = text.Length;
        var p = 0;
        while (p < n)
        {
            var i = p;
            while (i < n && IsIdentChar(text[i]))
            {
                i++;
            }

            var steps = 0;
            while (i + 1 < n && (text[i] == '.' || text[i] == '@') && IsIdentChar(text[i + 1]))
            {
                i += 2;
                while (i < n && IsIdentChar(text[i]))
                {
                    i++;
                }

                steps++;
            }

            if (steps > 0)
            {
                output.Add(' ');
                p = i;
            }
            else
            {
                output.Add(text[p]);
                p++;
            }
        }

        return output;
    }

    /// <summary><c>re.findall(r"[^\W\d_]+", text)</c>: maximal runs of letter-class code points.</summary>
    private static List<string> FindWords(List<int> text)
    {
        var words = new List<string>();
        var sb = new StringBuilder();
        foreach (var cp in text)
        {
            if (PyUnicodeTables.IsLetterClass(cp))
            {
                sb.Append(char.ConvertFromUtf32(cp));
            }
            else if (sb.Length > 0)
            {
                words.Add(sb.ToString());
                sb.Clear();
            }
        }

        if (sb.Length > 0)
        {
            words.Add(sb.ToString());
        }

        return words;
    }
}
