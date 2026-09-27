namespace Tau.Inference.Tests;

/// <summary>Locates the repository root (the directory holding Tau.slnx) and the models directory.</summary>
internal static class RepoRoot
{
    public static string Path { get; } = Find();

    /// <summary>TAU_MODELS_DIR if set, else &lt;repo&gt;/models.</summary>
    public static string Models =>
        Environment.GetEnvironmentVariable("TAU_MODELS_DIR") is { Length: > 0 } d ? d : System.IO.Path.Combine(Path, "models");

    /// <summary>&lt;repo&gt;/tests/fixtures.</summary>
    public static string Fixtures => System.IO.Path.Combine(Path, "tests", "fixtures");

    private static string Find()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(System.IO.Path.Combine(d.FullName, "Tau.slnx")))
                return d.FullName;
        throw new InvalidOperationException("repository root (Tau.slnx) not found above " + AppContext.BaseDirectory);
    }
}
