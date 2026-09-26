using System.Reflection;

namespace Tau.Contract;

/// <summary>Access to the pinned contract's machine-checkable schema text, embedded as resources.</summary>
public static class ContractSchemas
{
    /// <summary>The raw text of <c>contracts/systemone/2026-09-27/request.schema.json</c>.</summary>
    public static string RequestSchemaText { get; } = ReadResource("request.schema.json");

    /// <summary>The raw text of <c>contracts/systemone/2026-09-27/response.schema.json</c>.</summary>
    public static string ResponseSchemaText { get; } = ReadResource("response.schema.json");

    private static string ReadResource(string fileName)
    {
        var assembly = typeof(ContractSchemas).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("." + fileName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"embedded schema resource for '{fileName}' was not found");

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"embedded schema resource '{resourceName}' could not be opened");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
