using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Tau.Inference.Onnx;

namespace Tau.Inference.Tests.Onnx;

/// <summary>Serialises every test that touches the process-wide native resolver.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class OrtNativeCollection
{
    /// <summary>The collection name.</summary>
    public const string Name = "OrtNative";
}

/// <summary>
/// The provider spike (T026): load the committed <c>add.onnx</c> through the resolver on the provider named by
/// <c>TAU_ORT_PROVIDER</c> (cpu, cuda or directml; default cpu), with no fallback, and prove from ONNX Runtime's
/// own profile that the node ran on that provider. One provider per process, so
/// <c>scripts/provider-smoke.ps1</c> runs this class once per flavour.
/// </summary>
[Collection(OrtNativeCollection.Name)]
[Trait("Category", "Provider")]
public class ProviderSmokeTests
{
    internal static OrtProvider Requested =>
        Environment.GetEnvironmentVariable("TAU_ORT_PROVIDER") is { Length: > 0 } p ? OrtProviderNames.Parse(p) : OrtProvider.Cpu;

    internal static string NativeRoot =>
        Environment.GetEnvironmentVariable("TAU_NATIVE_DIR") is { Length: > 0 } d ? d : Path.Combine(TestData.RepoRoot, "native");

    internal static string ModelPath => TestData.Path("Onnx", "add.onnx");

    internal static void Configure() =>
        OrtNativeResolver.Configure(Requested, NativeRoot, Requested == OrtProvider.Cuda ? Path.Combine(NativeRoot, "cuda-deps") : null);

    [Fact]
    public void Runs_add_on_the_requested_provider_without_fallback()
    {
        Configure();
        var profileDir = Directory.CreateTempSubdirectory("tau-ort-profile-");
        try
        {
            using var session = OrtSessionFactory.Create(ModelPath, Requested, new OrtSessionSettings
            {
                EnableProfiling = true,
                ProfilePathPrefix = Path.Combine(profileDir.FullName, "add"),
            });
            Assert.Equal(Requested, session.Provider);
            Assert.Null(session.FallbackReason);

            float[] a = [1f, 2f, 3f, -4f, 0.5f, 1e-3f];
            float[] b = [10f, 20f, 30f, 4f, 0.25f, 2e-3f];
            using var results = session.Session.Run(
            [
                NamedOnnxValue.CreateFromTensor("a", new DenseTensor<float>(a, [2, 3])),
                NamedOnnxValue.CreateFromTensor("b", new DenseTensor<float>(b, [2, 3])),
            ]);
            var y = results.Single(r => r.Name == "y").AsTensor<float>();
            Assert.Equal([2, 3], y.Dimensions.ToArray());
            Assert.Equal(a.Zip(b, (p, q) => p + q).ToArray(), y.ToArray());

            // ONNX Runtime's profile names the provider every executed node ran on: the proof it was not silently
            // CPU. (DirectML compiles the graph into one "DmlFusedNode", so match all node events, not the name.)
            var profile = JsonNode.Parse(File.ReadAllText(session.Session.EndProfiling()))!.AsArray();
            var nodeProviders = profile
                .Select(e => e!.AsObject())
                .Where(e => e["cat"]?.GetValue<string>() == "Node")
                .Select(e => e["args"]?["provider"]?.GetValue<string>())
                .Distinct()
                .ToList();
            Assert.Equal([Requested.OrtName()], nodeProviders);
        }
        finally
        {
            profileDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Loads_the_native_libraries_from_the_configured_flavour()
    {
        Configure();
        using (OrtSessionFactory.Create(ModelPath, Requested))
        {
            var flavourDir = Path.GetFullPath(Path.Combine(NativeRoot, Requested.Flavour(), OrtNativeResolver.CurrentRid));
            Assert.Equal(Path.Combine(flavourDir, OperatingSystem.IsWindows() ? "onnxruntime.dll" : "libonnxruntime.so"), OrtNativeResolver.LibraryPath);

            var modules = Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
                .GroupBy(m => Path.GetFileName(m.FileName), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(m => m.FileName).ToList(), StringComparer.OrdinalIgnoreCase);

            void AssertLoadedFrom(string module, string dir)
            {
                Assert.True(modules.TryGetValue(module, out var paths), $"{module} is not loaded");
                Assert.All(paths!, p => Assert.StartsWith(Path.GetFullPath(dir), p, StringComparison.OrdinalIgnoreCase));
            }

            if (OperatingSystem.IsWindows())
            {
                AssertLoadedFrom("onnxruntime.dll", flavourDir);
                if (Requested == OrtProvider.DirectMl)
                {
                    // Not the old DirectML.dll in System32.
                    AssertLoadedFrom("DirectML.dll", flavourDir);
                }

                if (Requested == OrtProvider.Cuda)
                {
                    AssertLoadedFrom("onnxruntime_providers_cuda.dll", flavourDir);
                    foreach (var dll in new[] { "cudart64_12.dll", "cublas64_12.dll", "cublasLt64_12.dll", "cufft64_11.dll", "cudnn64_9.dll" })
                    {
                        AssertLoadedFrom(dll, Path.Combine(NativeRoot, "cuda-deps"));
                    }
                }
            }
        }
    }

    [Fact]
    public void Execution_provider_is_compiled_into_the_loaded_flavour()
    {
        Configure();
        Assert.Contains(Requested.OrtName(), OrtEnv.Instance().GetAvailableProviders());
    }

    [Fact]
    public void Cuda_dependencies_all_load_after_configure()
    {
        Assert.SkipWhen(Requested != OrtProvider.Cuda, "Only meaningful when TAU_ORT_PROVIDER=cuda.");
        Configure();
        Assert.Empty(OrtNativeResolver.ProbeCudaDependencies());
        Assert.NotEmpty(OrtNativeResolver.CudaLibraryDirectories);
    }

    [Fact]
    public void Configure_is_idempotent_and_refuses_a_second_flavour()
    {
        Configure();
        Configure(); // same provider and folder: no-op
        var other = Requested == OrtProvider.Cpu ? OrtProvider.Cuda : OrtProvider.Cpu;
        var ex = Assert.Throws<InvalidOperationException>(() => OrtNativeResolver.Configure(other, NativeRoot));
        Assert.Contains("one native flavour", ex.Message);
        Assert.Equal(Requested, OrtNativeResolver.Provider);
    }

    [Fact]
    public void Refuses_a_provider_the_loaded_flavour_lacks()
    {
        Configure();
        var other = Requested == OrtProvider.DirectMl ? OrtProvider.Cuda : OrtProvider.DirectMl;
        var ex = Assert.Throws<InvalidOperationException>(() => OrtSessionFactory.Create(ModelPath, other));
        Assert.Contains("one flavour per process", ex.Message);

        // CPU is in every flavour.
        using var cpu = OrtSessionFactory.Create(ModelPath, OrtProvider.Cpu);
        Assert.Equal(OrtProvider.Cpu, cpu.Provider);
    }

    [Fact]
    public void Unavailable_gpu_throws_actionably_or_falls_back_when_allowed()
    {
        Assert.SkipWhen(Requested == OrtProvider.Cpu, "Needs a GPU provider: run with TAU_ORT_PROVIDER=cuda or directml.");
        Configure();

        // No such GPU: the provider cannot be created on device 99.
        var noFallback = new OrtSessionSettings { DeviceId = 99 };
        var ex = Assert.Throws<OrtProviderUnavailableException>(() => OrtSessionFactory.Create(ModelPath, Requested, noFallback));
        Assert.Equal(Requested, ex.Provider);
        Assert.Contains(Requested.OrtName(), ex.Message);
        Assert.Contains("AllowCpuFallback", ex.Message);

        var warnings = new List<string>();
        using var session = OrtSessionFactory.Create(ModelPath, Requested, noFallback with { AllowCpuFallback = true, Warn = warnings.Add });
        Assert.Equal(OrtProvider.Cpu, session.Provider);
        Assert.Equal(Requested, session.RequestedProvider);
        Assert.NotNull(session.FallbackReason);
        Assert.Single(warnings);
    }
}
