using System.Text.Json;
using System.Text.Json.Nodes;
using Tau.Contract.Validation;

namespace Tau.Contract;

/// <summary>
/// The single entry point for turning request bytes into a validated <see cref="DecisionRequest"/>.
/// </summary>
/// <remarks>
/// <see cref="TryParse(string,out DecisionRequest?,out IReadOnlyList{ValidationProblem})"/> and its
/// stream overload never throw: malformed JSON, a body that isn't a JSON object, and every
/// contract violation are all reported as <see cref="ValidationProblem"/> entries instead.
/// </remarks>
public static class ContractParser
{
    /// <summary>
    /// Parses and validates a request body given as a JSON string.
    /// </summary>
    /// <param name="json">The request body.</param>
    /// <param name="request">The parsed request, when parsing succeeds; otherwise <c>null</c>.</param>
    /// <param name="problems">Every problem found, in no particular order; empty when parsing succeeds.</param>
    /// <returns><c>true</c> when <paramref name="request"/> was populated.</returns>
    public static bool TryParse(
        string json, out DecisionRequest? request, out IReadOnlyList<ValidationProblem> problems)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (Exception ex)
        {
            request = null;
            problems = [new ValidationProblem("$", $"malformed JSON: {ex.Message}")];
            return false;
        }

        return TryParseNode(root, out request, out problems);
    }

    /// <summary>
    /// Parses and validates a request body read from a stream.
    /// </summary>
    /// <param name="json">The request body stream.</param>
    /// <param name="request">The parsed request, when parsing succeeds; otherwise <c>null</c>.</param>
    /// <param name="problems">Every problem found, in no particular order; empty when parsing succeeds.</param>
    /// <returns><c>true</c> when <paramref name="request"/> was populated.</returns>
    public static bool TryParse(
        Stream json, out DecisionRequest? request, out IReadOnlyList<ValidationProblem> problems)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (Exception ex)
        {
            request = null;
            problems = [new ValidationProblem("$", $"malformed JSON: {ex.Message}")];
            return false;
        }

        return TryParseNode(root, out request, out problems);
    }

    private static bool TryParseNode(
        JsonNode? root, out DecisionRequest? request, out IReadOnlyList<ValidationProblem> problems)
    {
        var found = RequestValidator.Validate(root);
        if (found.Count > 0)
        {
            request = null;
            problems = found;
            return false;
        }

        try
        {
            request = root.Deserialize<DecisionRequest>(ContractJson.Options);
            problems = [];
            return true;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            // RequestValidator already accepted this body; reaching here would mean the
            // validator and the strongly-typed model have drifted apart. Reported, never thrown.
            request = null;
            problems = [new ValidationProblem("$", $"internal deserialisation error: {ex.Message}")];
            return false;
        }
    }
}
