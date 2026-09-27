using System.Text.Json.Nodes;

namespace Tau.Inference.Tests;

/// <summary>Locates committed test data and the fetched model directory.</summary>
internal static class TestData
{
    /// <summary>A path under the test output directory (data files are copied there by the project).</summary>
    public static string Path(params string[] parts) =>
        System.IO.Path.Combine([AppContext.BaseDirectory, .. parts]);

    /// <summary>Reads a JSONL fixture: the header object (first line) and every case after it.</summary>
    public static (JsonObject Header, List<JsonObject> Cases) ReadJsonl(string path)
    {
        var lines = File.ReadAllLines(path);
        Assert.True(lines.Length > 1, $"{path} has no cases");
        var header = JsonNode.Parse(lines[0])!.AsObject();
        Assert.True(header["header"]?.GetValue<bool>() == true, $"{path}: first line is not a header");
        var cases = lines.Skip(1).Where(l => l.Length > 0).Select(l => JsonNode.Parse(l)!.AsObject()).ToList();
        return (header, cases);
    }

    /// <summary>The repository root, found by walking up from the test output directory to Tau.slnx.</summary>
    public static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(System.IO.Path.Combine(dir.FullName, "Tau.slnx")))
            {
                dir = dir.Parent;
            }

            return dir?.FullName ?? throw new InvalidOperationException("Could not find the repo root (Tau.slnx) above " + AppContext.BaseDirectory);
        }
    }

    /// <summary>
    /// The fetched models directory: <c>TAU_MODELS_DIR</c> if set, otherwise <c>&lt;repo&gt;/models</c>.
    /// </summary>
    public static string ModelsDir =>
        Environment.GetEnvironmentVariable("TAU_MODELS_DIR") is { Length: > 0 } env
            ? env
            : System.IO.Path.Combine(RepoRoot, "models");

    /// <summary>A file under the models directory, failing (never skipping) with a fetch instruction if absent.</summary>
    public static string RequireModelFile(params string[] relative)
    {
        var path = System.IO.Path.Combine([ModelsDir, .. relative]);
        if (!File.Exists(path))
        {
            Assert.Fail($"Model file missing: {path}. Run scripts/fetch-models.ps1 (or set TAU_MODELS_DIR to a directory that holds src/<model id>/...).");
        }

        return path;
    }
}
