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
                Session = new OrtSessionSettings { Warn = m => log.LogWarning("{Message}", m) },
            };
            var engine = new OnnxDecisionEngine(settings);
            log.LogInformation("Tau engine ready: provider {Provider}, models {Models}",
                engine.ActualProvider?.ToString() ?? options.Provider, string.Join(", ", engine.Models.Select(m => m.Id)));
            return engine;
        });
        return services;
    }

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
