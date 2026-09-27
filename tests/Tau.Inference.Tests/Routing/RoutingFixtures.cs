using System.Text.Json.Nodes;

namespace Tau.Inference.Tests.Routing;

/// <summary>Loads <c>tests/fixtures/routing/routes.jsonl</c>, found by walking up to the repo root.</summary>
internal static class RoutingFixtures
{
    private static readonly Lazy<(JsonObject Header, IReadOnlyDictionary<string, JsonObject> Rows)> Loaded = new(Load);

    public static JsonObject Header => Loaded.Value.Header;

    public static IReadOnlyDictionary<string, JsonObject> Rows => Loaded.Value.Rows;

    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Tau.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Tau.slnx not found above " + AppContext.BaseDirectory);
    }

    private static (JsonObject, IReadOnlyDictionary<string, JsonObject>) Load()
    {
        var path = Path.Combine(RepoRoot(), "tests", "fixtures", "routing", "routes.jsonl");
        var lines = File.ReadAllLines(path).Where(l => l.Length > 0).ToArray();
        var header = JsonNode.Parse(lines[0])!.AsObject();
        var rows = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var line in lines.Skip(1))
        {
            var row = JsonNode.Parse(line)!.AsObject();
            rows.Add(row["id"]!.GetValue<string>(), row);
        }

        return (header, rows);
    }
}
