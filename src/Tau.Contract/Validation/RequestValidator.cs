using System.Text.Json.Nodes;

namespace Tau.Contract.Validation;

/// <summary>
/// Validates a raw <c>POST /v1/systemone</c> request body against the pinned contract snapshot
/// (R-10), collecting every problem rather than stopping at the first.
/// </summary>
/// <remarks>
/// Validation runs directly against the parsed JSON tree, independently of the strongly-typed
/// <see cref="DecisionRequest"/> model, so it can report every violation of a body that might not
/// even be deserialisable into that model (for example: two different criteria problems on two
/// different questions).
/// </remarks>
public static class RequestValidator
{
    private static readonly string[] AllowedTopLevelFields = ["model", "state", "questions"];
    private static readonly string[] AllowedQuestionFields = ["type", "instructions", "criteria"];
    private static readonly string[] AllowedQuestionTypes = ["choice", "score", "noul"];

    /// <summary>
    /// Parses <paramref name="json"/> and validates it. Never throws: malformed JSON is reported
    /// as a single problem at path <c>"$"</c>.
    /// </summary>
    /// <param name="json">The candidate request body.</param>
    /// <returns>Every problem found; empty when the body is valid.</returns>
    public static IReadOnlyList<ValidationProblem> Validate(string json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (Exception ex)
        {
            return [new ValidationProblem("$", $"malformed JSON: {ex.Message}")];
        }

        return Validate(root);
    }

    /// <summary>
    /// Validates an already-parsed request body.
    /// </summary>
    /// <param name="root">The parsed request body (may be <c>null</c>, representing a JSON <c>null</c> document).</param>
    /// <returns>Every problem found; empty when the body is valid.</returns>
    public static IReadOnlyList<ValidationProblem> Validate(JsonNode? root)
    {
        var problems = new List<ValidationProblem>();

        if (root is not JsonObject requestObject)
        {
            problems.Add(new ValidationProblem("$", "request body must be a JSON object"));
            return problems;
        }

        ValidateModel(requestObject, problems);
        ValidateState(requestObject, problems);
        ValidateQuestions(requestObject, problems);
        ValidateTopLevelUnknownFields(requestObject, problems);

        return problems;
    }

    private static void ValidateModel(JsonObject root, List<ValidationProblem> problems)
    {
        if (!root.ContainsKey("model"))
        {
            problems.Add(new ValidationProblem("model", "model is required"));
            return;
        }

        root.TryGetPropertyValue("model", out var node);
        if (!TryGetString(node, out var value) || value.Length < 1)
        {
            problems.Add(new ValidationProblem("model", "model must be a non-empty string"));
        }
    }

    private static void ValidateState(JsonObject root, List<ValidationProblem> problems)
    {
        // Any JSON value, including null, is valid — only the key's presence is checked.
        // ContainsKey (not "value is null") is what distinguishes "absent" from "present but null".
        if (!root.ContainsKey("state"))
        {
            problems.Add(new ValidationProblem("state", "state is required"));
        }
    }

    private static void ValidateQuestions(JsonObject root, List<ValidationProblem> problems)
    {
        if (!root.ContainsKey("questions"))
        {
            problems.Add(new ValidationProblem("questions", "questions is required"));
            return;
        }

        root.TryGetPropertyValue("questions", out var node);
        if (node is not JsonObject questions)
        {
            problems.Add(new ValidationProblem("questions", "questions must be an object"));
            return;
        }

        if (questions.Count < 1)
        {
            problems.Add(new ValidationProblem("questions", "questions must have at least 1 entry"));
        }
        else if (questions.Count > 50)
        {
            problems.Add(new ValidationProblem("questions", $"questions must have at most 50 entries, got {questions.Count}"));
        }

        foreach (var (key, value) in questions)
        {
            ValidateQuestion($"questions.{key}", value, problems);
        }
    }

    private static void ValidateQuestion(string path, JsonNode? value, List<ValidationProblem> problems)
    {
        if (value is not JsonObject question)
        {
            problems.Add(new ValidationProblem(path, "question must be an object"));
            return;
        }

        foreach (var (field, _) in question)
        {
            if (!AllowedQuestionFields.Contains(field))
            {
                problems.Add(new ValidationProblem($"{path}.{field}", $"unknown field '{field}'"));
            }
        }

        var type = ValidateQuestionType(path, question, problems);

        if (!question.ContainsKey("instructions"))
        {
            problems.Add(new ValidationProblem($"{path}.instructions", "instructions is required"));
        }
        else
        {
            question.TryGetPropertyValue("instructions", out var instructions);
            if (Classify(instructions) is not (JsonKind.String or JsonKind.Object or JsonKind.Array))
            {
                problems.Add(new ValidationProblem($"{path}.instructions", "instructions must be a string, object or array"));
            }
        }

        // The question's own type couldn't be established, so which criteria shape applies is
        // unknown; the type problem above already covers it.
        if (type is null)
        {
            return;
        }

        switch (type)
        {
            case "choice":
                ValidateChoiceCriteria(path, question, problems);
                break;
            case "score":
                ValidateScoreCriteria(path, question, problems);
                break;
            case "noul":
                ValidateNoulCriteria(path, question, problems);
                break;
        }
    }

    private static string? ValidateQuestionType(string path, JsonObject question, List<ValidationProblem> problems)
    {
        if (!question.ContainsKey("type"))
        {
            problems.Add(new ValidationProblem($"{path}.type", "type is required"));
            return null;
        }

        question.TryGetPropertyValue("type", out var typeNode);
        if (TryGetString(typeNode, out var typeValue) && AllowedQuestionTypes.Contains(typeValue))
        {
            return typeValue;
        }

        var shown = TryGetString(typeNode, out var raw) ? raw : Classify(typeNode).ToString().ToLowerInvariant();
        problems.Add(new ValidationProblem($"{path}.type", $"type must be one of choice, score, noul, got '{shown}'"));
        return null;
    }

    private static void ValidateChoiceCriteria(string path, JsonObject question, List<ValidationProblem> problems)
    {
        var criteriaPath = $"{path}.criteria";
        if (!question.ContainsKey("criteria"))
        {
            problems.Add(new ValidationProblem(criteriaPath, "criteria is required"));
            return;
        }

        question.TryGetPropertyValue("criteria", out var node);
        if (node is not JsonObject criteria)
        {
            problems.Add(new ValidationProblem(criteriaPath, "criteria must be an object"));
            return;
        }

        if (criteria.Count < 1 || criteria.Count > 255)
        {
            problems.Add(new ValidationProblem(criteriaPath, $"criteria must have 1-255 entries, got {criteria.Count}"));
        }

        foreach (var (optionKey, optionValue) in criteria)
        {
            if (Classify(optionValue) is not (JsonKind.String or JsonKind.Object or JsonKind.Array or JsonKind.Null))
            {
                problems.Add(new ValidationProblem($"{criteriaPath}.{optionKey}", "criteria value must be a string, object, array or null"));
            }
        }
    }

    private static void ValidateScoreCriteria(string path, JsonObject question, List<ValidationProblem> problems)
    {
        var criteriaPath = $"{path}.criteria";
        if (!question.ContainsKey("criteria"))
        {
            problems.Add(new ValidationProblem(criteriaPath, "criteria is required"));
            return;
        }

        question.TryGetPropertyValue("criteria", out var node);
        if (node is not JsonArray levels)
        {
            problems.Add(new ValidationProblem(criteriaPath, "criteria must be an array"));
            return;
        }

        if (levels.Count < 2 || levels.Count > 10)
        {
            problems.Add(new ValidationProblem(criteriaPath, $"criteria must have 2-10 levels, got {levels.Count}"));
        }

        for (var i = 0; i < levels.Count; i++)
        {
            if (Classify(levels[i]) is not (JsonKind.String or JsonKind.Object or JsonKind.Array))
            {
                problems.Add(new ValidationProblem($"{criteriaPath}[{i}]", "criteria item must be a string, object or array"));
            }
        }
    }

    private static void ValidateNoulCriteria(string path, JsonObject question, List<ValidationProblem> problems)
    {
        if (!question.ContainsKey("criteria"))
        {
            // Optional for noul questions.
            return;
        }

        var criteriaPath = $"{path}.criteria";
        question.TryGetPropertyValue("criteria", out var node);
        if (node is not JsonObject criteria)
        {
            problems.Add(new ValidationProblem(criteriaPath, "criteria must be an object"));
            return;
        }

        foreach (var (key, value) in criteria)
        {
            if (!string.Equals(key, "true", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(key, "false", StringComparison.OrdinalIgnoreCase))
            {
                problems.Add(new ValidationProblem($"{criteriaPath}.{key}", $"criteria key must be 'true' or 'false' (case-insensitive), got '{key}'"));
                continue;
            }

            if (Classify(value) is not (JsonKind.String or JsonKind.Object or JsonKind.Array or JsonKind.Null))
            {
                problems.Add(new ValidationProblem($"{criteriaPath}.{key}", "criteria value must be a string, object, array or null"));
            }
        }
    }

    private static void ValidateTopLevelUnknownFields(JsonObject root, List<ValidationProblem> problems)
    {
        foreach (var (field, _) in root)
        {
            if (AllowedTopLevelFields.Contains(field))
            {
                continue;
            }

            if (field.StartsWith("x-tau-", StringComparison.Ordinal))
            {
                continue;
            }

            problems.Add(new ValidationProblem(field, $"unknown field '{field}'"));
        }
    }

    private static bool TryGetString(JsonNode? node, out string value)
    {
        if (node is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var stringValue))
        {
            value = stringValue;
            return true;
        }

        value = "";
        return false;
    }

    private enum JsonKind
    {
        Null,
        String,
        Number,
        Bool,
        Object,
        Array,
    }

    private static JsonKind Classify(JsonNode? node)
    {
        if (node is null)
        {
            return JsonKind.Null;
        }

        if (node is JsonObject)
        {
            return JsonKind.Object;
        }

        if (node is JsonArray)
        {
            return JsonKind.Array;
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out _))
            {
                return JsonKind.String;
            }

            if (value.TryGetValue<bool>(out _))
            {
                return JsonKind.Bool;
            }

            return JsonKind.Number;
        }

        return JsonKind.Number;
    }
}
