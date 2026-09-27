using System.Text.Json;
using System.Text.Json.Nodes;
using Tau.Contract;

namespace Tau.Conformance;

/// <summary>The result of checking one side's response body for one fixture.</summary>
/// <param name="ContractOk">
/// <c>true</c> when the body satisfies both the schema (strict or lenient, per the caller) and
/// every semantic check below. This is exactly "no contract failure" for that side.
/// </param>
/// <param name="Errors">Every reason <paramref name="ContractOk"/> is false; empty when it is true.</param>
/// <param name="Extensions">
/// Fields present beyond the contract's own shape. Recorded only when the caller validated
/// leniently — under strict validation the same fields would already appear in <paramref name="Errors"/>.
/// </param>
internal sealed record ValidationOutcome(bool ContractOk, IReadOnlyList<string> Errors, IReadOnlyList<string> Extensions);

/// <summary>
/// Checks one <c>/v1/systemone</c> success response against the pinned response schema and against
/// the semantics FR-024 and R-13 require: one answer per question, matching types, choice/score
/// values that are actually possible answers to the question asked, and probabilities that sum to
/// one. The same logic runs for Tau and for a peer; only <paramref name="strict"/> differs.
/// </summary>
internal static class ResponseValidator
{
    private static readonly HashSet<string> TopLevelFields = ["model", "answers", "usage"];
    private static readonly HashSet<string> UsageFields = ["input_tokens", "output_tokens"];
    private static readonly HashSet<string> NoulAnswerFields = ["type", "noul"];
    private static readonly HashSet<string> ChoiceAnswerFields = ["type", "choice", "probabilities", "confidence"];
    private static readonly HashSet<string> ScoreAnswerFields = ["type", "score", "legend", "probabilities", "confidence"];

    public static ValidationOutcome ValidateSuccessBody(string rawBody, DecisionRequest request, bool strict)
    {
        var errors = new List<string>();
        var extensions = new List<string>();

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(rawBody);
        }
        catch (JsonException ex)
        {
            return new ValidationOutcome(false, [$"response body is not valid JSON: {ex.Message}"], extensions);
        }

        using (document)
        {
            var schema = strict ? ResponseSchemas.Strict : ResponseSchemas.Lenient;
            if (!schema.Evaluate(document.RootElement).IsValid)
            {
                errors.Add(strict
                    ? "response does not strictly match response.schema.json (the contract forbids fields outside it)"
                    : "response does not match response.schema.json even once additional fields are permitted");
            }
        }

        var root = JsonNode.Parse(rawBody) as JsonObject;
        if (root is null)
        {
            errors.Add("response body is not a JSON object");
            return new ValidationOutcome(false, errors, extensions);
        }

        CollectExtensions(root, strict, errors, extensions);
        CheckSemantics(root, request, errors);

        return new ValidationOutcome(errors.Count == 0, errors, extensions);
    }

    private static void CollectExtensions(JsonObject root, bool strict, List<string> errors, List<string> extensions)
    {
        Report(root.Select(kv => kv.Key), TopLevelFields, "top-level field", strict, errors, extensions);

        if (root["usage"] is JsonObject usage)
        {
            Report(usage.Select(kv => kv.Key), UsageFields, "usage field", strict, errors, extensions);
        }

        if (root["answers"] is not JsonObject answers)
        {
            return;
        }

        foreach (var (key, value) in answers)
        {
            if (value is not JsonObject answer)
            {
                continue;
            }

            var known = JsonHelpers.AsString(answer["type"]) switch
            {
                "noul" => NoulAnswerFields,
                "choice" => ChoiceAnswerFields,
                "score" => ScoreAnswerFields,
                _ => (HashSet<string>?)null,
            };

            if (known is not null)
            {
                Report(answer.Select(kv => kv.Key), known, $"answers.{key} field", strict, errors, extensions);
            }
        }
    }

    private static void Report(
        IEnumerable<string> actualKeys,
        HashSet<string> knownKeys,
        string label,
        bool strict,
        List<string> errors,
        List<string> extensions)
    {
        foreach (var key in actualKeys)
        {
            if (knownKeys.Contains(key))
            {
                continue;
            }

            if (strict)
            {
                errors.Add($"unexpected {label} '{key}' (the contract forbids response extensions)");
            }
            else
            {
                extensions.Add($"{label} '{key}'");
            }
        }
    }

    private static void CheckSemantics(JsonObject root, DecisionRequest request, List<string> errors)
    {
        if (root["answers"] is not JsonObject answers)
        {
            errors.Add("answers is missing or not an object");
            return;
        }

        var requestKeys = new HashSet<string>(request.Questions.Keys);
        var answerKeys = new HashSet<string>(answers.Select(kv => kv.Key));

        foreach (var missing in requestKeys.Except(answerKeys))
        {
            errors.Add($"question '{missing}': no answer in the response");
        }

        foreach (var unexpected in answerKeys.Except(requestKeys))
        {
            errors.Add($"answers contains '{unexpected}', which is not one of the request's questions");
        }

        foreach (var (key, question) in request.Questions)
        {
            if (answers[key] is not JsonObject answer)
            {
                continue; // already reported as missing above
            }

            var actualType = JsonHelpers.AsString(answer["type"]);
            if (actualType != question.Type)
            {
                errors.Add($"question '{key}': expected answer type '{question.Type}', got '{actualType ?? "<missing>"}'");
                continue; // the wrong shape of answer makes the field checks below meaningless
            }

            switch (question)
            {
                case ChoiceQuestion choiceQuestion:
                    CheckChoiceAnswer(key, choiceQuestion, answer, errors);
                    break;
                case ScoreQuestion scoreQuestion:
                    CheckScoreAnswer(key, scoreQuestion, answer, errors);
                    break;
                case NoulQuestion:
                    CheckNoulAnswer(key, answer, errors);
                    break;
            }
        }
    }

    private static void CheckChoiceAnswer(string key, ChoiceQuestion question, JsonObject answer, List<string> errors)
    {
        var criteriaKeys = new HashSet<string>(question.Criteria.Select(kv => kv.Key));

        var choice = JsonHelpers.AsString(answer["choice"]);
        if (choice is null)
        {
            errors.Add($"question '{key}': choice is missing or not a string");
        }
        else if (!criteriaKeys.Contains(choice))
        {
            errors.Add($"question '{key}': choice '{choice}' is not one of the question's criteria keys");
        }

        CheckProbabilities(key, answer, criteriaKeys, "criteria keys", errors);
    }

    private static void CheckScoreAnswer(string key, ScoreQuestion question, JsonObject answer, List<string> errors)
    {
        var levelCount = question.Criteria.Count;
        var maxIndex = levelCount - 1;

        var score = JsonHelpers.AsDouble(answer["score"]);
        if (score is null)
        {
            errors.Add($"question '{key}': score is missing or not a number");
        }
        else if (score < 0 || score > maxIndex)
        {
            errors.Add($"question '{key}': score {score} is outside [0, {maxIndex}]");
        }

        var expectedIndices = Enumerable.Range(0, levelCount).Select(i => i.ToString()).ToHashSet();
        CheckProbabilities(key, answer, expectedIndices, $"level indices 0..{maxIndex}", errors);
    }

    private static void CheckNoulAnswer(string key, JsonObject answer, List<string> errors)
    {
        var noul = JsonHelpers.AsDouble(answer["noul"]);
        if (noul is null)
        {
            errors.Add($"question '{key}': noul is missing or not a number");
        }
        else if (noul < 0 || noul > 1)
        {
            errors.Add($"question '{key}': noul {noul} is outside [0, 1]");
        }
    }

    private static void CheckProbabilities(
        string key, JsonObject answer, HashSet<string> expectedKeys, string expectedLabel, List<string> errors)
    {
        if (answer["probabilities"] is not JsonObject probabilities)
        {
            errors.Add($"question '{key}': probabilities is missing or not an object");
            return;
        }

        var actualKeys = new HashSet<string>(probabilities.Select(kv => kv.Key));
        if (!actualKeys.SetEquals(expectedKeys))
        {
            errors.Add(
                $"question '{key}': probability keys {{{string.Join(",", actualKeys.OrderBy(k => k))}}} "
                + $"do not match the {expectedLabel} {{{string.Join(",", expectedKeys.OrderBy(k => k))}}}");
        }

        double sum = 0;
        var allNumeric = true;
        foreach (var (_, value) in probabilities)
        {
            var d = JsonHelpers.AsDouble(value);
            if (d is null)
            {
                allNumeric = false;
                continue;
            }

            sum += d.Value;
        }

        if (!allNumeric)
        {
            errors.Add($"question '{key}': probabilities contains a non-numeric value");
        }
        else if (Math.Abs(sum - 1.0) > 1e-3)
        {
            errors.Add($"question '{key}': probabilities sum to {sum:0.######}, not 1 within 1e-3");
        }
    }
}
