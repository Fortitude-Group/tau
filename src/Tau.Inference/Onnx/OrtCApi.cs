using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;

namespace Tau.Inference.Onnx;

/// <summary>
/// The few ONNX Runtime C API calls the managed 1.24.4 API does not expose, reached through the
/// <c>OrtApi</c> function table of the library <see cref="OrtNativeResolver"/> loaded.
/// </summary>
/// <remarks>
/// The table is append-only and ABI-stable, and the indices below are those of the ORT 1.24.4 C header
/// (<c>onnxruntime_c_api.h</c>). The managed assembly declares the same table as its <c>OrtApi</c> struct, and a
/// test checks these indices against that struct's field order, so a version bump that moved them would fail.
/// </remarks>
internal static class OrtCApi
{
    /// <summary>ORT_API_VERSION of ONNX Runtime 1.24.</summary>
    internal const uint ApiVersion = 24;

    /// <summary><c>const char* GetErrorMessage(const OrtStatus*)</c>.</summary>
    internal const int GetErrorMessageIndex = 2;

    /// <summary><c>void ReleaseStatus(OrtStatus*)</c>.</summary>
    internal const int ReleaseStatusIndex = 93;

    /// <summary><c>OrtStatus* SetDeterministicCompute(OrtSessionOptions*, bool)</c>, since ORT 1.17.</summary>
    internal const int SetDeterministicComputeIndex = 273;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetApiFn(uint version);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr SetDeterministicComputeFn(IntPtr options, [MarshalAs(UnmanagedType.U1)] bool value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetErrorMessageFn(IntPtr status);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ReleaseStatusFn(IntPtr status);

    /// <summary>
    /// Asks ONNX Runtime to prefer deterministic kernels for sessions built from <paramref name="options"/>
    /// (<c>OrtApi::SetDeterministicCompute</c>).
    /// </summary>
    internal static void SetDeterministicCompute(SessionOptions options, bool value)
    {
        var api = Api();
        var fn = Marshal.GetDelegateForFunctionPointer<SetDeterministicComputeFn>(Entry(api, SetDeterministicComputeIndex));
        bool added = false;
        options.DangerousAddRef(ref added);
        try
        {
            ThrowIfError(api, fn(options.DangerousGetHandle(), value), "SetDeterministicCompute");
        }
        finally
        {
            if (added) options.DangerousRelease();
        }
    }

    /// <summary>The <c>OrtApi</c> table pointer, from <c>OrtGetApiBase()-&gt;GetApi(ApiVersion)</c>.</summary>
    internal static IntPtr Api()
    {
        var library = OrtNativeResolver.LibraryPath
            ?? throw new InvalidOperationException("OrtNativeResolver.Configure must run before the ONNX Runtime C API is used.");
        var handle = NativeLibrary.Load(library); // already loaded: returns the same module
        var getApiBase = NativeLibrary.GetExport(handle, "OrtGetApiBase");
        var apiBase = Marshal.GetDelegateForFunctionPointer<GetApiBaseFn>(getApiBase)();
        var getApi = Marshal.GetDelegateForFunctionPointer<GetApiFn>(Marshal.ReadIntPtr(apiBase, 0));
        var api = getApi(ApiVersion);
        return api != IntPtr.Zero
            ? api
            : throw new InvalidOperationException($"{library} does not support ONNX Runtime C API version {ApiVersion}.");
    }

    /// <summary>The function pointer at <paramref name="index"/> in the table.</summary>
    internal static IntPtr Entry(IntPtr api, int index) => Marshal.ReadIntPtr(api, index * IntPtr.Size);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetApiBaseFn();

    private static void ThrowIfError(IntPtr api, IntPtr status, string call)
    {
        if (status == IntPtr.Zero)
        {
            return;
        }

        var message = Marshal.PtrToStringUTF8(Marshal.GetDelegateForFunctionPointer<GetErrorMessageFn>(Entry(api, GetErrorMessageIndex))(status));
        Marshal.GetDelegateForFunctionPointer<ReleaseStatusFn>(Entry(api, ReleaseStatusIndex))(status);
        throw new InvalidOperationException($"ONNX Runtime {call} failed: {message}");
    }
}
