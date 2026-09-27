using Microsoft.ML.OnnxRuntime;

namespace Tau.Inference.Onnx;

/// <summary>Session settings for <see cref="OrtSessionFactory"/>. The defaults favour reproducible output over speed.</summary>
public sealed record OrtSessionSettings
{
    /// <summary>
    /// Graph optimisation level. Default <see cref="GraphOptimizationLevel.ORT_ENABLE_BASIC"/>: constant folding and
    /// redundant-node removal only, no operator fusions that change the arithmetic.
    /// </summary>
    public GraphOptimizationLevel GraphOptimizationLevel { get; init; } = GraphOptimizationLevel.ORT_ENABLE_BASIC;

    /// <summary>
    /// Threads for work inside one operator. Fixed rather than ONNX Runtime's machine-dependent default, so the way
    /// CPU work is split does not change between machines. Default 4.
    /// </summary>
    public int IntraOpThreads { get; init; } = 4;

    /// <summary>Threads across operators. Default 1 (the graph runs sequentially).</summary>
    public int InterOpThreads { get; init; } = 1;

    /// <summary>GPU ordinal for CUDA and DirectML. Default 0.</summary>
    public int DeviceId { get; init; }

    /// <summary>
    /// When the requested provider cannot be created, use CPU (and report it) instead of throwing
    /// <see cref="OrtProviderUnavailableException"/>. Default false.
    /// </summary>
    public bool AllowCpuFallback { get; init; }

    /// <summary>Write an ONNX Runtime profile (which records the provider each node ran on). Default false.</summary>
    public bool EnableProfiling { get; init; }

    /// <summary>Path prefix for the profile file when <see cref="EnableProfiling"/> is set.</summary>
    public string? ProfilePathPrefix { get; init; }

    /// <summary>Receives warnings, such as a CPU fallback. The host wires this to its logger.</summary>
    public Action<string>? Warn { get; init; }
}

/// <summary>An ONNX Runtime session and the provider it actually runs on.</summary>
public sealed class OrtSession : IDisposable
{
    internal OrtSession(InferenceSession session, OrtProvider requested, OrtProvider active, string? fallbackReason)
    {
        Session = session;
        RequestedProvider = requested;
        Provider = active;
        FallbackReason = fallbackReason;
    }

    /// <summary>The ONNX Runtime session.</summary>
    public InferenceSession Session { get; }

    /// <summary>The provider that was asked for.</summary>
    public OrtProvider RequestedProvider { get; }

    /// <summary>The provider the session was created with (CPU after a fallback).</summary>
    public OrtProvider Provider { get; }

    /// <summary>Why the session fell back to CPU, or null if it did not.</summary>
    public string? FallbackReason { get; }

    /// <inheritdoc />
    public void Dispose() => Session.Dispose();
}

/// <summary>
/// Creates ONNX Runtime sessions with deterministic options on a chosen execution provider, after
/// <see cref="OrtNativeResolver.Configure"/> has picked the native flavour.
/// </summary>
/// <remarks>
/// <para>
/// Every session: the configured graph optimisation level (basic by default), sequential execution, fixed
/// intra- and inter-op thread counts, and ONNX Runtime's deterministic-compute flag. CUDA adds
/// <c>cudnn_conv_algo_search=DEFAULT</c> (a fixed algorithm rather than a timing-based pick) and
/// <c>use_tf32=0</c> (full FP32 matmuls on Ampere and later, which the FP32 parity tolerances assume).
/// DirectML turns off memory patterns, as DirectML requires.
/// </para>
/// <para>
/// If the provider cannot be appended or the session cannot be created on it, the factory throws
/// <see cref="OrtProviderUnavailableException"/> with the cause and the fix, unless
/// <see cref="OrtSessionSettings.AllowCpuFallback"/> is set, in which case it warns and builds a CPU session.
/// </para>
/// </remarks>
public static class OrtSessionFactory
{
    /// <summary>Creates a session for <paramref name="modelPath"/> on <paramref name="provider"/>.</summary>
    /// <param name="modelPath">Path to the <c>.onnx</c> file.</param>
    /// <param name="provider">The execution provider to run on.</param>
    /// <param name="settings">Session settings; null for the defaults.</param>
    /// <returns>The session and the provider it runs on.</returns>
    /// <exception cref="FileNotFoundException">The model file is missing.</exception>
    /// <exception cref="InvalidOperationException">
    /// <see cref="OrtNativeResolver.Configure"/> has not run, or it loaded a flavour that lacks <paramref name="provider"/>.
    /// </exception>
    /// <exception cref="OrtProviderUnavailableException">The provider could not be created and fallback is off.</exception>
    public static OrtSession Create(string modelPath, OrtProvider provider, OrtSessionSettings? settings = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        settings ??= new OrtSessionSettings();
        ArgumentOutOfRangeException.ThrowIfLessThan(settings.IntraOpThreads, 1, nameof(settings.IntraOpThreads));
        ArgumentOutOfRangeException.ThrowIfLessThan(settings.InterOpThreads, 1, nameof(settings.InterOpThreads));
        ArgumentOutOfRangeException.ThrowIfNegative(settings.DeviceId, nameof(settings.DeviceId));
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException($"ONNX model not found: {Path.GetFullPath(modelPath)}", modelPath);
        }

        var configured = OrtNativeResolver.Provider
            ?? throw new InvalidOperationException("Call OrtNativeResolver.Configure before creating ONNX Runtime sessions.");
        if (provider != OrtProvider.Cpu && provider != configured)
        {
            throw new InvalidOperationException(
                $"The {configured.Flavour()} ONNX Runtime natives are loaded, which do not include the {provider.Flavour()} provider. "
                + $"Configure {provider.Flavour()} at startup instead (one flavour per process).");
        }

        if (provider == OrtProvider.Cpu)
        {
            return new OrtSession(CreateSession(modelPath, OrtProvider.Cpu, settings), OrtProvider.Cpu, OrtProvider.Cpu, null);
        }

        try
        {
            return new OrtSession(CreateSession(modelPath, provider, settings), provider, provider, null);
        }
        catch (OrtProviderUnavailableException ex) when (settings.AllowCpuFallback)
        {
            var reason = ex.Message;
            settings.Warn?.Invoke($"{provider.OrtName()} unavailable, falling back to CPU: {reason}");
            return new OrtSession(CreateSession(modelPath, OrtProvider.Cpu, settings), provider, OrtProvider.Cpu, reason);
        }
    }

    private static InferenceSession CreateSession(string modelPath, OrtProvider provider, OrtSessionSettings settings)
    {
        using var options = new SessionOptions
        {
            GraphOptimizationLevel = settings.GraphOptimizationLevel,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            IntraOpNumThreads = settings.IntraOpThreads,
            InterOpNumThreads = settings.InterOpThreads,
            LogId = $"tau-{provider.Flavour()}",
        };
        OrtCApi.SetDeterministicCompute(options, true);

        if (settings.EnableProfiling)
        {
            options.EnableProfiling = true;
            if (settings.ProfilePathPrefix is not null)
            {
                options.ProfileOutputPathPrefix = settings.ProfilePathPrefix;
            }
        }

        try
        {
            switch (provider)
            {
                case OrtProvider.Cuda:
                    using (var cuda = new OrtCUDAProviderOptions())
                    {
                        cuda.UpdateOptions(new Dictionary<string, string>
                        {
                            ["device_id"] = settings.DeviceId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            ["cudnn_conv_algo_search"] = "DEFAULT",
                            ["use_tf32"] = "0",
                        });
                        options.AppendExecutionProvider_CUDA(cuda);
                    }

                    break;

                case OrtProvider.DirectMl:
                    options.EnableMemoryPattern = false;
                    options.AppendExecutionProvider_DML(settings.DeviceId);
                    break;
            }
        }
        catch (Exception ex) when (provider != OrtProvider.Cpu && ex is OnnxRuntimeException or DllNotFoundException or EntryPointNotFoundException)
        {
            throw Unavailable(provider, "could not be added to the session", ex);
        }

        try
        {
            return new InferenceSession(modelPath, options);
        }
        catch (OnnxRuntimeException ex) when (provider != OrtProvider.Cpu)
        {
            throw Unavailable(provider, "could not create a session", ex);
        }
    }

    private static OrtProviderUnavailableException Unavailable(OrtProvider provider, string what, Exception inner)
    {
        var fix = provider switch
        {
            OrtProvider.Cuda => "CUDA needs an NVIDIA GPU and driver, the cuda natives (scripts/fetch-natives.ps1) and the CUDA 12 / cuDNN 9 "
                + "libraries (scripts/fetch-cuda.ps1, passed to OrtNativeResolver.Configure as cudaDepsDir). "
                + ProbeSummary(),
            OrtProvider.DirectMl => "DirectML needs Windows 10 1903 or later with a DirectX 12 GPU and the directml natives (scripts/fetch-natives.ps1).",
            _ => string.Empty,
        };
        return new OrtProviderUnavailableException(
            provider,
            $"The {provider.OrtName()} {what}: {inner.Message} {fix} Set AllowCpuFallback to run on CPU instead.",
            inner);
    }

    private static string ProbeSummary()
    {
        var failures = OrtNativeResolver.ProbeCudaDependencies();
        return failures.Count == 0
            ? "All CUDA libraries load, so the failure is in the device or driver."
            : "Libraries that do not load: " + string.Join("; ", failures) + ".";
    }
}
