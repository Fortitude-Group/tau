using System.Text.Json.Nodes;
using Tau.Contract;

namespace Tau.Conformance;

/// <summary>
/// One committed request fixture from <c>tests/conformance/requests/*.json</c>: a name, a
/// description, the exact bytes to POST to <c>/v1/systemone</c>, and the status the pinned
/// contract expects back.
/// </summary>
internal sealed class RequestFixture
{
    public required string FileName { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required int ExpectedStatus { get; init; }

    /// <summary>The exact request body to send, already serialised (or, for the malformed-JSON
    /// fixture, the literal raw text stored under <c>raw_body</c>).</summary>
    public required string WireBody { get; init; }

    /// <summary>
    /// The fixture's request, parsed through Tau's own <see cref="ContractParser"/>. Populated only
    /// for fixtures that expect <c>200</c> (a 422 fixture's request is deliberately invalid, so
    /// there is nothing to parse); used to drive the response semantic checks.
    /// </summary>
    public DecisionRequest? ParsedRequest { get; init; }

    /// <summary>Loads and sorts every <c>*.json</c> fixture in <paramref name="dir"/>.</summary>
    public static IReadOnlyList<RequestFixture> LoadAll(string dir)
    {
        if (!Directory.Exists(dir))
        {
            throw new DirectoryNotFoundException($"conformance requests directory not found: '{dir}'");
        }

        var fixtures = new List<RequestFixture>();
        foreach (var path in Directory.EnumerateFiles(dir, "*.json").OrderBy(p => p, StringComparer.Ordinal))
        {
            fixtures.Add(Load(path));
        }

        if (fixtures.Count == 0)
        {
            throw new InvalidOperationException($"no *.json fixtures found under '{dir}'");
        }

        return fixtures;
    }

    private static RequestFixture Load(string path)
    {
        var text = File.ReadAllText(path);
        var root = JsonNode.Parse(text) as JsonObject
            ?? throw new InvalidOperationException($"fixture '{path}' is not a JSON object");

        var name = root["name"]?.ToString() ?? Path.GetFileNameWithoutExtension(path);
        var description = root["description"]?.ToString() ?? "";

        if (root["expect"] is not JsonObject expect || expect["status"] is not JsonNode statusNode)
        {
            throw new InvalidOperationException($"fixture '{path}' is missing 'expect.status'");
        }

        var status = statusNode.GetValue<int>();

        string wireBody;
        DecisionRequest? parsed = null;

        if (root["raw_body"] is JsonNode rawBodyNode)
        {
            wireBody = rawBodyNode.GetValue<string>();
        }
        else if (root["request"] is JsonNode requestNode)
        {
            wireBody = requestNode.ToJsonString();

            if (status == 200)
            {
                if (!ContractParser.TryParse(wireBody, out parsed, out var problems))
                {
                    var detail = string.Join("; ", problems.Select(p => $"{p.Path}: {p.Problem}"));
                    throw new InvalidOperationException(
                        $"fixture '{path}' expects 200 but its request fails Tau's own contract parser: {detail}");
                }
            }
        }
        else
        {
            throw new InvalidOperationException($"fixture '{path}' has neither 'request' nor 'raw_body'");
        }

        return new RequestFixture
        {
            FileName = Path.GetFileName(path),
            Name = name,
            Description = description,
            ExpectedStatus = status,
            WireBody = wireBody,
            ParsedRequest = parsed,
        };
    }
}
