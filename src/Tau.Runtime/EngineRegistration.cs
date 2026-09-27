using Tau.Inference.Engine;
using Tau.Inference.Onnx;

namespace Tau.Runtime;

/// <summary>Registers the decision engine.</summary>
public static class EngineRegistration
{
    /// <summary>
    /// Adds the ONNX decision engine configured by <paramref name="options"/>. The engine is created when first
    /// resolved (Program resolves it at start-up), so a bad model, provider or calibrator stops the process before
    /// it serves a request.
    /// </summary>
    /// <param name="services">Services.</param>
    /// <param name="options">Runtime options.</param>
    public static IServiceCollection AddTauEngine(this IServiceCollection services, TauOptions options)
    {
        services.AddSingleton<IDecisionEngine>(sp =>
        {
            var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Tau.Engine");
            var root = FindRoot();
            var settings = new EngineSettings
            {
                ModelsDirectory = options.ModelsDirectory ?? Path.Combine(root, "models"),
                NativeDirectory = options.NativeDirectory ?? Path.Combine(root, "native"),
                CudaDepsDirectory = options.CudaDepsDirectory ?? Path.Combine(root, "native", "cuda-deps"),
                Provider = OrtProviderNames.Parse(options.Provider),
                AllowCpuFallback = options.AllowCpuFallback,
                Models = options.Models,
                Preload = options.Preload,
                CalibratorsDirectory = options.CalibratorsDirectory,
                VonMaxTokens = options.VonMaxTokens,
                Session = new OrtSessionSettings
                {
                    Warn = m => log.LogWarning("{Message}", m),
                    GraphOptimizationLevel = options.GraphOptimization.ToLowerInvariant() switch
                    {
                        "extended" => Microsoft.ML.OnnxRuntime.GraphOptimizationLevel.ORT_ENABLE_EXTENDED,
                        "all" => Microsoft.ML.OnnxRuntime.GraphOptimizationLevel.ORT_ENABLE_ALL,
                        _ => Microsoft.ML.OnnxRuntime.GraphOptimizationLevel.ORT_ENABLE_BASIC,
                    },
                    IntraOpThreads = options.IntraOpThreads > 0 ? options.IntraOpThreads : PhysicalCores(),
                    EnableMemoryPattern = options.MemoryPattern,
                },
            };
            var engine = new OnnxDecisionEngine(settings);
            log.LogInformation("Tau engine ready: provider {Provider}, models {Models}",
                engine.ActualProvider?.ToString() ?? options.Provider, string.Join(", ", engine.Models.Select(m => m.Id)));
            return engine;
        });
        return services;
    }

    /// <summary>Physical core count (ONNX Runtime's own default for intra-op threads), falling back to logical/2.</summary>
    private static int PhysicalCores() => Math.Max(1, Environment.ProcessorCount / 2);

    /// <summary>
    /// The directory holding <c>models/</c> and <c>native/</c>: next to the executable when published, otherwise
    /// the repository root found by walking up to <c>Tau.slnx</c>.
    /// </summary>
    private static string FindRoot()
    {
        var baseDir = AppContext.BaseDirectory;
        if (Directory.Exists(Path.Combine(baseDir, "native"))) return baseDir;
        for (var d = new DirectoryInfo(baseDir); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "Tau.slnx"))) return d.FullName;
        return Directory.GetCurrentDirectory();
    }
}
