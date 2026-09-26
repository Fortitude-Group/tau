using System.Text.Json.Serialization;

namespace Tau.Contract;

/// <summary>The 422 response body Tau returns when a request fails validation.</summary>
public sealed class ValidationErrorBody
{
    /// <summary>The error payload.</summary>
    [JsonPropertyName("error")]
    public required ValidationErrorPayload Error { get; init; }
}

/// <summary>The <c>error</c> object inside a <see cref="ValidationErrorBody"/>.</summary>
public sealed class ValidationErrorPayload
{
    /// <summary>Always <c>"validation_error"</c>.</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "validation_error";

    /// <summary>A human-readable summary of the failure.</summary>
    [JsonPropertyName("message")]
    public required string Message { get; init; }

    /// <summary>Every offending field, so a caller can fix them all in one pass.</summary>
    [JsonPropertyName("details")]
    public required IReadOnlyList<ValidationProblem> Details { get; init; }
}
