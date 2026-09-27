using System.Text;
using Tau.Calibration;
using Tau.Workbench.Spec;

namespace Tau.Workbench.Frontier;

/// <summary>Fixed caps on frontier work (constitution XVI).</summary>
public static class FrontierLimits
{
    /// <summary>The most held-out items per dataset that may ever be exported for a frontier answer.</summary>
    public const int MaxHeldOutItems = 1000;

    /// <summary>
    /// The most calibration items per dataset that may be exported, when the spec scores against the
    /// frontier model (<c>data.reference: frontier</c>) and the calibrators need frontier labels to fit on.
    /// </summary>
    public const int MaxCalibrationItems = 1000;

    /// <summary>Items per pending batch file (research R-05).</summary>
    public const int BatchSize = 200;

    /// <summary>The salt for choosing the alternative-wording subset. Seed 42, as every split.</summary>
    public const string AltSubsetSeed = "42";
}

/// <summary>
/// The versioned per-item frontier prompts. A version's text never changes once answers exist for it:
/// a new wording is a new version, because the cache is keyed by prompt version.
/// <list type="bullet">
/// <item><c>v1</c> (primary): the task first, then the allowed answers with their descriptions, then the item.</item>
/// <item><c>v1-alt</c>: a different wording and order for the agreement check: the item first, then the
/// question phrased as a request, then the answers as "key = description".</item>
/// </list>
/// Both end by asking for exactly one allowed answer and nothing else.
/// </summary>
public static class PromptTemplates
{
    /// <summary>The primary prompt version.</summary>
    public const string Primary = "v1";

    /// <summary>The alternative wording used on a subset.</summary>
    public const string Alternative = "v1-alt";

    /// <summary>Every known prompt version.</summary>
    public static readonly IReadOnlyList<string> Known = [Primary, Alternative];

    /// <summary>Renders the prompt for one item.</summary>
    /// <param name="version">A known prompt version.</param>
    /// <param name="question">The spec's question.</param>
    /// <param name="text">The item's text.</param>
    /// <exception cref="ArgumentException">The version is unknown.</exception>
    public static string Render(string version, QuestionSpec question, string text)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(text);
        return version switch
        {
            Primary => RenderV1(question, text),
            Alternative => RenderV1Alt(question, text),
            _ => throw new ArgumentException($"Unknown prompt version '{version}'.", nameof(version)),
        };
    }

    private static string RenderV1(QuestionSpec q, string text)
    {
        var sb = new StringBuilder();
        sb.Append("You are labelling one item for an evaluation dataset.\n\n");
        sb.Append("Task: ").Append(q.Instructions.Trim()).Append("\n\n");
        switch (q.Type)
        {
            case QuestionType.Choice:
                sb.Append("Allowed answers (reply with the key on the left):\n");
                foreach (var o in q.Options)
                {
                    sb.Append("- ").Append(o.Key);
                    if (!string.IsNullOrWhiteSpace(o.Description))
                    {
                        sb.Append(": ").Append(o.Description.Trim());
                    }

                    sb.Append('\n');
                }

                break;
            case QuestionType.Score:
                sb.Append("Allowed answers are level numbers, from the lowest level (0) to the highest:\n");
                foreach (var o in q.Options)
                {
                    sb.Append("- ").Append(o.Key).Append(": ").Append(o.Description.Trim()).Append('\n');
                }

                break;
            default:
                sb.Append("Allowed answers: true or false.\n");
                AppendNoulOutcomes(sb, q, "- true: ", "- false: ");
                break;
        }

        sb.Append("\nItem:\n<<<\n").Append(text).Append("\n>>>\n\n");
        sb.Append("Reply with exactly one allowed answer and nothing else: no explanation, no punctuation, no quotes.");
        return sb.ToString();
    }

    private static string RenderV1Alt(QuestionSpec q, string text)
    {
        var sb = new StringBuilder();
        sb.Append("Read the following text carefully.\n\n\"\"\"\n").Append(text).Append("\n\"\"\"\n\n");
        sb.Append("Question about the text above: ").Append(q.Instructions.Trim()).Append('\n');
        switch (q.Type)
        {
            case QuestionType.Choice:
                sb.Append("Pick the single best fit from this list. Each line reads code = meaning:\n");
                foreach (var o in q.Options)
                {
                    sb.Append(o.Key);
                    if (!string.IsNullOrWhiteSpace(o.Description))
                    {
                        sb.Append(" = ").Append(o.Description.Trim());
                    }

                    sb.Append('\n');
                }

                sb.Append("\nAnswer with the code only, exactly as written in the list, and nothing else.");
                break;
            case QuestionType.Score:
                sb.Append("Rate it on this scale. Each line reads number = level, where a higher number means a higher level:\n");
                for (int i = q.Options.Count - 1; i >= 0; i--)
                {
                    sb.Append(q.Options[i].Key).Append(" = ").Append(q.Options[i].Description.Trim()).Append('\n');
                }

                sb.Append("\nAnswer with the number only and nothing else.");
                break;
            default:
                sb.Append("Decide whether the statement holds for this text.\n");
                AppendNoulOutcomes(sb, q, "true = ", "false = ");
                sb.Append("\nAnswer with the single word true or false and nothing else.");
                break;
        }

        return sb.ToString();
    }

    private static void AppendNoulOutcomes(StringBuilder sb, QuestionSpec q, string truePrefix, string falsePrefix)
    {
        foreach (var (key, prefix) in new[] { ("true", truePrefix), ("false", falsePrefix) })
        {
            var d = q.Options.FirstOrDefault(o => o.Key == key)?.Description;
            if (!string.IsNullOrWhiteSpace(d))
            {
                sb.Append(prefix).Append(d.Trim()).Append('\n');
            }
        }
    }
}
