namespace Tau.Inference.Onnx;

/// <summary>
/// The ONNX Runtime execution provider a process runs on. Each maps to one native flavour under
/// <c>native/&lt;cpu|cuda|directml&gt;/&lt;rid&gt;/</c> (see <see cref="OrtNativeResolver"/>).
/// </summary>
public enum OrtProvider
{
    /// <summary>The CPU execution provider (<c>native/cpu</c>). Every flavour also contains it.</summary>
    Cpu,

    /// <summary>NVIDIA CUDA 12 with cuDNN 9 (<c>native/cuda</c>, plus <c>native/cuda-deps</c> from <c>scripts/fetch-cuda.ps1</c>).</summary>
    Cuda,

    /// <summary>DirectML on any DirectX 12 GPU, Windows only (<c>native/directml</c>).</summary>
    DirectMl,
}

/// <summary>Conversions between <see cref="OrtProvider"/> and its config and folder names.</summary>
public static class OrtProviderNames
{
    /// <summary>The native flavour folder (and config value): <c>cpu</c>, <c>cuda</c> or <c>directml</c>.</summary>
    /// <param name="provider">The provider.</param>
    /// <returns>The lower-case flavour name.</returns>
    public static string Flavour(this OrtProvider provider) => provider switch
    {
        OrtProvider.Cpu => "cpu",
        OrtProvider.Cuda => "cuda",
        OrtProvider.DirectMl => "directml",
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown ONNX Runtime provider."),
    };

    /// <summary>The name ONNX Runtime uses for the provider, e.g. in profiles: <c>CUDAExecutionProvider</c>.</summary>
    /// <param name="provider">The provider.</param>
    /// <returns>The ONNX Runtime execution provider name.</returns>
    public static string OrtName(this OrtProvider provider) => provider switch
    {
        OrtProvider.Cpu => "CPUExecutionProvider",
        OrtProvider.Cuda => "CUDAExecutionProvider",
        OrtProvider.DirectMl => "DmlExecutionProvider",
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown ONNX Runtime provider."),
    };

    /// <summary>Parses a config value (<c>cpu</c>, <c>cuda</c>, <c>directml</c>; case-insensitive; <c>dml</c> accepted).</summary>
    /// <param name="value">The config value.</param>
    /// <returns>The provider.</returns>
    /// <exception cref="ArgumentException">The value names no provider.</exception>
    public static OrtProvider Parse(string value) => value?.Trim().ToLowerInvariant() switch
    {
        "cpu" => OrtProvider.Cpu,
        "cuda" => OrtProvider.Cuda,
        "directml" or "dml" => OrtProvider.DirectMl,
        _ => throw new ArgumentException($"Unknown ONNX Runtime provider '{value}'. Use cpu, cuda or directml.", nameof(value)),
    };
}
