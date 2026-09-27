using Microsoft.ML.OnnxRuntime;
using Tau.Inference.Onnx;

namespace Tau.Inference.Tests.Onnx;

/// <summary>ONNX Runtime plumbing that needs no native library loaded.</summary>
[Collection(OrtNativeCollection.Name)]
public class OrtUnitTests
{
    [Theory]
    [InlineData("cpu", OrtProvider.Cpu)]
    [InlineData("CUDA", OrtProvider.Cuda)]
    [InlineData(" directml ", OrtProvider.DirectMl)]
    [InlineData("dml", OrtProvider.DirectMl)]
    public void Parses_provider_names(string value, OrtProvider expected) =>
        Assert.Equal(expected, OrtProviderNames.Parse(value));

    [Theory]
    [InlineData("")]
    [InlineData("gpu")]
    [InlineData("tensorrt")]
    public void Rejects_unknown_provider_names(string value) =>
        Assert.Throws<ArgumentException>(() => OrtProviderNames.Parse(value));

    [Fact]
    public void Maps_providers_to_flavours_and_ort_names()
    {
        Assert.Equal(("cpu", "CPUExecutionProvider"), (OrtProvider.Cpu.Flavour(), OrtProvider.Cpu.OrtName()));
        Assert.Equal(("cuda", "CUDAExecutionProvider"), (OrtProvider.Cuda.Flavour(), OrtProvider.Cuda.OrtName()));
        Assert.Equal(("directml", "DmlExecutionProvider"), (OrtProvider.DirectMl.Flavour(), OrtProvider.DirectMl.OrtName()));
        Assert.Throws<ArgumentOutOfRangeException>(() => ((OrtProvider)42).Flavour());
    }

    [Fact]
    public void C_api_indices_match_the_managed_assemblys_OrtApi_table()
    {
        // Microsoft.ML.OnnxRuntime declares the C function table as a struct whose fields are in table order.
        var fields = typeof(InferenceSession).Assembly.GetType("Microsoft.ML.OnnxRuntime.OrtApi", throwOnError: true)!
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            .Select(f => f.Name)
            .ToList();
        Assert.Equal(OrtCApi.GetErrorMessageIndex, fields.IndexOf("GetErrorMessage"));
        Assert.Equal(OrtCApi.ReleaseStatusIndex, fields.IndexOf("ReleaseStatus"));
        Assert.Equal(OrtCApi.SetDeterministicComputeIndex, fields.IndexOf("SetDeterministicCompute"));
    }

    [Fact]
    public void Configure_validates_its_folder()
    {
        var missing = Path.Combine(Path.GetTempPath(), "tau-no-natives-" + Guid.NewGuid().ToString("N"));
        if (OrtNativeResolver.Provider is null)
        {
            var ex = Assert.Throws<DirectoryNotFoundException>(() => OrtNativeResolver.Configure(OrtProvider.Cpu, missing));
            Assert.Contains("fetch-natives", ex.Message);
            Assert.Null(OrtNativeResolver.Provider); // a failed configure leaves nothing behind
        }
        else
        {
            // Another test already chose this process's flavour; a different folder is refused outright.
            Assert.Throws<InvalidOperationException>(() => OrtNativeResolver.Configure(OrtNativeResolver.Provider.Value, missing));
        }

        Assert.Throws<ArgumentException>(() => OrtNativeResolver.Configure(OrtProvider.Cpu, " "));
    }

    [Fact]
    public void Configure_rejects_a_missing_cuda_deps_folder()
    {
        Assert.SkipWhen(OrtNativeResolver.Provider is not null, "A flavour is already loaded in this process; this runs before any is.");

        var root = Directory.CreateTempSubdirectory("tau-natives-");
        try
        {
            var flavour = Directory.CreateDirectory(Path.Combine(root.FullName, "cuda", OrtNativeResolver.CurrentRid));
            File.WriteAllText(Path.Combine(flavour.FullName, OperatingSystem.IsWindows() ? "onnxruntime.dll" : "libonnxruntime.so"), "not a library");
            var ex = Assert.Throws<DirectoryNotFoundException>(() =>
                OrtNativeResolver.Configure(OrtProvider.Cuda, root.FullName, Path.Combine(root.FullName, "no-cuda-deps")));
            Assert.Contains("fetch-cuda", ex.Message);
            Assert.Null(OrtNativeResolver.Provider);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Factory_validates_its_arguments()
    {
        Assert.Throws<FileNotFoundException>(() => OrtSessionFactory.Create(Path.Combine(Path.GetTempPath(), "missing.onnx"), OrtProvider.Cpu));
        Assert.Throws<ArgumentException>(() => OrtSessionFactory.Create(" ", OrtProvider.Cpu));
        var model = TestData.Path("Onnx", "add.onnx");
        Assert.Throws<ArgumentOutOfRangeException>(() => OrtSessionFactory.Create(model, OrtProvider.Cpu, new OrtSessionSettings { IntraOpThreads = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => OrtSessionFactory.Create(model, OrtProvider.Cpu, new OrtSessionSettings { DeviceId = -1 }));
        if (OrtNativeResolver.Provider is null)
        {
            var ex = Assert.Throws<InvalidOperationException>(() => OrtSessionFactory.Create(model, OrtProvider.Cpu));
            Assert.Contains("OrtNativeResolver.Configure", ex.Message);
        }
    }

    [Fact]
    public void Defaults_favour_reproducibility()
    {
        var s = new OrtSessionSettings();
        Assert.Equal(GraphOptimizationLevel.ORT_ENABLE_BASIC, s.GraphOptimizationLevel);
        Assert.Equal(4, s.IntraOpThreads);
        Assert.Equal(1, s.InterOpThreads);
        Assert.Equal(0, s.DeviceId);
        Assert.False(s.AllowCpuFallback);
    }

    [Fact]
    public void Unavailable_exception_carries_the_provider()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new OrtProviderUnavailableException(OrtProvider.Cuda, "msg", inner);
        Assert.Equal(OrtProvider.Cuda, ex.Provider);
        Assert.Same(inner, ex.InnerException);
    }
}
