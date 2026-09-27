using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;

namespace Tau.Inference.Onnx;

/// <summary>
/// Points the managed ONNX Runtime assembly at one native flavour, chosen at startup, so a single binary runs
/// on CPU, CUDA or DirectML (docs/DECISIONS.md, "ONNX Runtime pinned at 1.24.4").
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Configure"/> registers a <see cref="NativeLibrary.SetDllImportResolver"/> on
/// <c>Microsoft.ML.OnnxRuntime</c> that answers its <c>onnxruntime</c> P/Invokes with
/// <c>&lt;nativeRoot&gt;/&lt;flavour&gt;/&lt;rid&gt;/onnxruntime.dll</c> (or <c>libonnxruntime.so</c>), and loads that
/// library straight away so a broken install fails at startup. It must run before anything touches ONNX
/// Runtime, and a process can hold only one flavour: calling it again with the same provider and folder is a
/// no-op, and with anything else is an error.
/// </para>
/// <para>
/// ONNX Runtime loads its provider libraries (<c>onnxruntime_providers_cuda.dll</c> and friends) from its own
/// folder, but their dependencies are found by the normal OS search. So for CUDA the resolver prepends the
/// CUDA 12 / cuDNN 9 library folders under <c>cudaDepsDir</c> to <c>PATH</c> on Windows (cuDNN also loads its
/// sub-libraries by name at run time, which only a search-path entry satisfies) and preloads the libraries
/// by full path on Linux, where the loader reads <c>LD_LIBRARY_PATH</c> only at process start. For DirectML it
/// preloads the flavour's <c>DirectML.dll</c>, because Windows ships an older one in System32 that the
/// delay-loaded import would otherwise pick up.
/// </para>
/// </remarks>
public static class OrtNativeResolver
{
    /// <summary>The P/Invoke library name the managed ONNX Runtime assembly uses.</summary>
    public const string OrtLibraryName = "onnxruntime";

    private static readonly Lock Gate = new();
    private static Configuration? _current;

    private sealed record Configuration(OrtProvider Provider, string FlavourDirectory, string LibraryPath, IntPtr Handle, IReadOnlyList<string> CudaLibraryDirectories);

    /// <summary>The configured provider, or null before <see cref="Configure"/>.</summary>
    public static OrtProvider? Provider
    {
        get
        {
            lock (Gate)
            {
                return _current?.Provider;
            }
        }
    }

    /// <summary>The full path of the loaded ONNX Runtime library, or null before <see cref="Configure"/>.</summary>
    public static string? LibraryPath
    {
        get
        {
            lock (Gate)
            {
                return _current?.LibraryPath;
            }
        }
    }

    /// <summary>The CUDA library folders made discoverable (empty unless CUDA was configured with a deps folder).</summary>
    public static IReadOnlyList<string> CudaLibraryDirectories
    {
        get
        {
            lock (Gate)
            {
                return _current?.CudaLibraryDirectories ?? [];
            }
        }
    }

    /// <summary>The runtime identifier of the native folder for this process: <c>win-x64</c> or <c>linux-x64</c>.</summary>
    /// <exception cref="PlatformNotSupportedException">Any other OS or architecture.</exception>
    public static string CurrentRid
    {
        get
        {
            if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
            {
                throw new PlatformNotSupportedException($"Tau ships ONNX Runtime natives for x64 only, not {RuntimeInformation.ProcessArchitecture}.");
            }

            if (OperatingSystem.IsWindows()) return "win-x64";
            if (OperatingSystem.IsLinux()) return "linux-x64";
            throw new PlatformNotSupportedException($"Tau ships ONNX Runtime natives for Windows and Linux only, not {RuntimeInformation.OSDescription}.");
        }
    }

    /// <summary>
    /// Selects and loads the native ONNX Runtime flavour for <paramref name="provider"/>. Idempotent for the
    /// same provider and folder.
    /// </summary>
    /// <param name="provider">The execution provider the process will use.</param>
    /// <param name="nativeRoot">The folder holding <c>&lt;flavour&gt;/&lt;rid&gt;/</c> (the repo's <c>native/</c>, from <c>scripts/fetch-natives.ps1</c>).</param>
    /// <param name="cudaDepsDir">
    /// For <see cref="OrtProvider.Cuda"/>: the folder <c>scripts/fetch-cuda.ps1</c> installed the NVIDIA wheels
    /// into (<c>native/cuda-deps</c>). Null to rely on a system CUDA install. Ignored for other providers.
    /// </param>
    /// <exception cref="InvalidOperationException">A different flavour is already configured in this process.</exception>
    /// <exception cref="DirectoryNotFoundException">The flavour folder or the CUDA deps folder is missing.</exception>
    /// <exception cref="FileNotFoundException">The ONNX Runtime library (or DirectML.dll) is missing from the flavour folder.</exception>
    /// <exception cref="PlatformNotSupportedException">DirectML off Windows, or an unsupported OS or architecture.</exception>
    /// <exception cref="DllNotFoundException">The library exists but the OS could not load it.</exception>
    public static void Configure(OrtProvider provider, string nativeRoot, string? cudaDepsDir = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nativeRoot);
        var flavourDir = Path.GetFullPath(Path.Combine(nativeRoot, provider.Flavour(), CurrentRid));

        lock (Gate)
        {
            if (_current is { } existing)
            {
                if (existing.Provider == provider && PathsEqual(existing.FlavourDirectory, flavourDir))
                {
                    return;
                }

                throw new InvalidOperationException(
                    $"ONNX Runtime is already configured for {existing.Provider.Flavour()} from {existing.FlavourDirectory}; "
                    + $"a process can load only one native flavour, so {provider.Flavour()} from {flavourDir} needs a new process.");
            }

            if (provider == OrtProvider.DirectMl && !OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("DirectML is available on Windows only.");
            }

            if (!Directory.Exists(flavourDir))
            {
                throw new DirectoryNotFoundException(
                    $"No {provider.Flavour()} ONNX Runtime natives at {flavourDir}. Run scripts/fetch-natives.ps1 (or point the native root at a folder laid out as <flavour>/<rid>/).");
            }

            var libraryPath = Path.Combine(flavourDir, OperatingSystem.IsWindows() ? "onnxruntime.dll" : "libonnxruntime.so");
            if (!File.Exists(libraryPath))
            {
                throw new FileNotFoundException(
                    $"{libraryPath} is missing. Run scripts/fetch-natives.ps1 -Force to restore the {provider.Flavour()} natives.", libraryPath);
            }

            IReadOnlyList<string> cudaDirs = [];
            if (provider == OrtProvider.Cuda && cudaDepsDir is not null)
            {
                cudaDirs = PrepareCuda(cudaDepsDir);
            }

            if (provider == OrtProvider.DirectMl)
            {
                var directMl = Path.Combine(flavourDir, "DirectML.dll");
                if (!File.Exists(directMl))
                {
                    throw new FileNotFoundException(
                        $"{directMl} is missing; the DirectML flavour needs the DirectML 1.15 redistributable next to onnxruntime.dll. Run scripts/fetch-natives.ps1 -Force.", directMl);
                }

                NativeLibrary.Load(directMl);
            }

            var handle = NativeLibrary.Load(libraryPath);
            try
            {
                NativeLibrary.SetDllImportResolver(typeof(InferenceSession).Assembly, Resolve);
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException(
                    "Something else already set a native-library resolver on the ONNX Runtime assembly, so Tau cannot choose the provider flavour.", ex);
            }

            _current = new Configuration(provider, flavourDir, libraryPath, handle, cudaDirs);
        }
    }

    /// <summary>
    /// Tries to load each CUDA library the CUDA provider imports (and cuDNN's sub-libraries) by name, the way
    /// ONNX Runtime will, and reports the ones that fail with the OS error. Empty means all loaded.
    /// </summary>
    /// <returns>One line per library that failed to load.</returns>
    public static IReadOnlyList<string> ProbeCudaDependencies()
    {
        string[] names = OperatingSystem.IsWindows()
            ? ["cudart64_12.dll", "cublasLt64_12.dll", "cublas64_12.dll", "cufft64_11.dll", "cudnn64_9.dll", "cudnn_graph64_9.dll",
               "cudnn_ops64_9.dll", "cudnn_cnn64_9.dll", "cudnn_adv64_9.dll", "cudnn_heuristic64_9.dll", "cudnn_engines_precompiled64_9.dll",
               "cudnn_engines_runtime_compiled64_9.dll", "nvcuda.dll"]
            : ["libcudart.so.12", "libcublasLt.so.12", "libcublas.so.12", "libcufft.so.11", "libcudnn.so.9", "libcuda.so.1"];
        var failures = new List<string>();
        foreach (var name in names)
        {
            try
            {
                NativeLibrary.Load(name);
            }
            catch (DllNotFoundException ex)
            {
                failures.Add($"{name}: {ex.Message}");
            }
        }

        return failures;
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, OrtLibraryName, StringComparison.Ordinal))
        {
            return IntPtr.Zero; // default probing for anything else
        }

        return _current?.Handle ?? IntPtr.Zero;
    }

    private static IReadOnlyList<string> PrepareCuda(string cudaDepsDir)
    {
        var root = Path.GetFullPath(cudaDepsDir);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"CUDA dependency folder {root} does not exist. Run scripts/fetch-cuda.ps1.");
        }

        // The NVIDIA wheels put their libraries in nvidia/<component>/bin (Windows) or nvidia/<component>/lib (Linux).
        var pattern = OperatingSystem.IsWindows() ? "*.dll" : "*.so*";
        var dirs = Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
            .Select(f => Path.GetDirectoryName(f)!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (dirs.Count == 0)
        {
            throw new DirectoryNotFoundException($"No CUDA libraries under {root}. Run scripts/fetch-cuda.ps1.");
        }

        if (OperatingSystem.IsWindows())
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            var existing = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            var add = dirs.Where(d => !existing.Contains(d, StringComparer.OrdinalIgnoreCase)).ToList();
            if (add.Count > 0)
            {
                Environment.SetEnvironmentVariable("PATH", string.Join(Path.PathSeparator, add.Append(path)));
            }
        }
        else
        {
            // Dependency order, so each library's own imports are already resident when it loads; a second
            // pass picks up any cuDNN sub-library whose sibling loaded after it on the first. Not verified on
            // a Linux GPU host yet (the reference machine is Windows).
            string[] order = ["libcudart.so.12", "libcublasLt.so.12", "libcublas.so.12", "libcufft.so.11", "libcurand.so.10", "libnvrtc.so.12", "libcudnn"];
            var files = dirs.SelectMany(d => Directory.EnumerateFiles(d, "*.so*")).ToList();
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (var prefix in order)
                {
                    foreach (var file in files.Where(f => Path.GetFileName(f).StartsWith(prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
                    {
                        NativeLibrary.TryLoad(file, out _);
                    }
                }
            }
        }

        return dirs;
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
