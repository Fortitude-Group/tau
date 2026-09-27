namespace Tau.Inference.Onnx;

/// <summary>
/// The requested execution provider could not be created, and CPU fallback was not allowed.
/// The message says what failed and what to do about it.
/// </summary>
public sealed class OrtProviderUnavailableException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="provider">The provider that could not be created.</param>
    /// <param name="message">What failed and how to fix it.</param>
    /// <param name="innerException">The ONNX Runtime error, if any.</param>
    public OrtProviderUnavailableException(OrtProvider provider, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Provider = provider;
    }

    /// <summary>The provider that could not be created.</summary>
    public OrtProvider Provider { get; }
}
