using Json.Schema;
using Tau.Contract;

namespace Tau.Runtime.Tests;

/// <summary>
/// The contract response schema, built once: JsonSchema.Net registers schemas globally by <c>$id</c> and refuses to
/// register the same id twice, so every test must share this instance.
/// </summary>
internal static class TestSchemas
{
    public static readonly JsonSchema Response = JsonSchema.FromText(ContractSchemas.ResponseSchemaText);
}
