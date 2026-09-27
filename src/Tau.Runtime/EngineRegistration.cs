using Tau.Inference.Engine;

namespace Tau.Runtime;

/// <summary>Registers the decision engine.</summary>
public static class EngineRegistration
{
    /// <summary>Adds the ONNX decision engine configured by <paramref name="options"/>.</summary>
    /// <param name="services">Services.</param>
    /// <param name="options">Runtime options.</param>
    public static IServiceCollection AddTauEngine(this IServiceCollection services, TauOptions options)
    {
        services.AddSingleton<IDecisionEngine>(_ =>
            throw new NotSupportedException("the ONNX decision engine is wired in by T038"));
        return services;
    }
}
