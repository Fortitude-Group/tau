using System.Text.Json;

namespace Tau.Contract;

/// <summary>Serialisation settings shared by every consumer of the contract types.</summary>
public static class ContractJson
{
    /// <summary>
    /// The <see cref="JsonSerializerOptions"/> to use for all contract (de)serialisation. Every
    /// wire name is given explicitly via <c>JsonPropertyName</c>, so no naming policy is applied.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = null,
        DictionaryKeyPolicy = null,
        WriteIndented = false,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
    };
}
