using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Tau.Contract;

namespace Tau.Bench;

/// <summary>The committed, fixed workload: one support-ticket state and ten questions in a fixed order.</summary>
internal sealed class Workload
{
    private readonly JsonObject _root;
    private readonly JsonArray _questions;

    private Workload(JsonObject root, string path)
    {
        _root = root;
        Path = path;
        _questions = root["questions"]?.AsArray() ?? throw new InvalidDataException($"{path}: no 'questions' array");
        if (_questions.Count < 10) throw new InvalidDataException($"{path}: expected 10 questions, found {_questions.Count}");
        // Hash the canonical (compact) JSON, not the file bytes, so line-ending conversion doesn't change the identity.
        Sha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToJsonString())));
        StateWords = CountWords(root["state"]);
    }

    public string Path { get; }
    public string Name => (string?)_root["name"] ?? "unnamed";
    public string Description => (string?)_root["description"] ?? "";
    public string Sha256 { get; }
    public int StateWords { get; }

    /// <summary>Question types of the first <paramref name="q"/> questions, e.g. <c>choice, score, noul, choice</c>.</summary>
    public IReadOnlyList<string> Types(int q) => _questions.Take(q).Select(x => (string)x!["type"]!).ToArray();

    public static Workload Load(string? path = null)
    {
        path ??= System.IO.Path.Combine(AppContext.BaseDirectory, "workload.json");
        var root = JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? throw new InvalidDataException($"{path}: empty");
        return new Workload(root, path);
    }

    /// <summary>The contract request body for the first <paramref name="q"/> questions, pinned to <paramref name="model"/>.</summary>
    public string RequestJson(string model, int q)
    {
        var questions = new JsonObject();
        foreach (var node in _questions.Take(q))
        {
            var src = node!.AsObject();
            var body = new JsonObject { ["type"] = src["type"]!.DeepClone(), ["instructions"] = src["instructions"]!.DeepClone() };
            if (src["criteria"] is { } c) body["criteria"] = c.DeepClone();
            questions[(string)src["key"]!] = body;
        }
        return new JsonObject { ["model"] = model, ["state"] = _root["state"]!.DeepClone(), ["questions"] = questions }.ToJsonString();
    }

    /// <summary>Parses and validates <see cref="RequestJson"/> through the contract parser the Runtime uses.</summary>
    public DecisionRequest Request(string model, int q)
    {
        if (!ContractParser.TryParse(RequestJson(model, q), out var request, out var problems) || request is null)
            throw new InvalidDataException("workload request is not contract-valid: "
                + string.Join("; ", problems.Select(p => $"{p.Path}: {p.Problem}")));
        return request;
    }

    private static int CountWords(JsonNode? node) => node switch
    {
        JsonObject o => o.Sum(kv => CountWords(kv.Value)),
        JsonArray a => a.Sum(CountWords),
        JsonValue v => (v.ToString() ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
        _ => 0,
    };
}
