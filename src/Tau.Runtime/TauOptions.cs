namespace Tau.Runtime;

/// <summary>Runtime configuration, bound from the <c>Tau</c> section (appsettings, env <c>Tau__*</c>, or <c>--Tau:*</c>).</summary>
public sealed class TauOptions
{
    /// <summary>Execution provider: <c>cpu</c>, <c>cuda</c> or <c>directml</c>.</summary>
    public string Provider { get; set; } = "cpu";

    /// <summary>If the configured GPU provider can't start, run on CPU instead of refusing to start.</summary>
    public bool AllowCpuFallback { get; set; }

    /// <summary>Directory holding one sub-directory per model package (default: <c>models</c> next to the repo root or executable).</summary>
    public string? ModelsDirectory { get; set; }

    /// <summary>Directory of the ONNX Runtime native flavours (<c>native/&lt;flavour&gt;/&lt;rid&gt;</c>).</summary>
    public string? NativeDirectory { get; set; }

    /// <summary>Directory holding the CUDA 12 / cuDNN 9 runtime DLLs (from <c>scripts/fetch-cuda.ps1</c>).</summary>
    public string? CudaDepsDirectory { get; set; }

    /// <summary>Model ids to serve. Empty means every package found in <see cref="ModelsDirectory"/>.</summary>
    public List<string> Models { get; set; } = [];

    /// <summary>Load every served model at start-up instead of on first use.</summary>
    public bool Preload { get; set; } = true;

    /// <summary>Most model sessions resident at once (least recently used is evicted). The reference router's default is 2.</summary>
    public int MaxLoaded { get; set; } = 4;

    /// <summary>Directory of <c>*.calibrator.json</c> files to apply (none when unset).</summary>
    public string? CalibratorsDirectory { get; set; }

    /// <summary>Largest request body accepted, in bytes.</summary>
    public long MaxRequestBytes { get; set; } = 1 << 20;

    /// <summary>Von packed-sequence cap in tokens (the reference allows 8192; see DECISIONS).</summary>
    public int VonMaxTokens { get; set; } = 4096;

    /// <summary>ONNX Runtime graph optimisation: <c>basic</c> (default), <c>extended</c> or <c>all</c>.</summary>
    public string GraphOptimization { get; set; } = "basic";

    /// <summary>CPU intra-op threads per session (0 = one per physical core).</summary>
    public int IntraOpThreads { get; set; }

    /// <summary>ONNX Runtime memory-pattern planning per input shape.</summary>
    public bool MemoryPattern { get; set; } = true;

    /// <summary>OTLP endpoint for traces and metrics; exporter off when unset.</summary>
    public string? OtlpEndpoint { get; set; }
}
