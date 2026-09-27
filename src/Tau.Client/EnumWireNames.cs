using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace Tau.Client;

/// <summary>
/// Resolves the wire name each member of an enum is sent/received as in a choice question's
/// criteria. The convention, checked in order per member:
/// 1. <see cref="JsonStringEnumMemberNameAttribute"/> (System.Text.Json, .NET 9+);
/// 2. <see cref="EnumMemberAttribute"/> (<c>System.Runtime.Serialization</c>), for enums shared
///    with other serialisers;
/// 3. the plain C# member name.
/// </summary>
internal static class EnumWireNames
{
    /// <summary>Every member of <typeparamref name="TEnum"/>, in declaration order, paired with its wire name.</summary>
    public static IReadOnlyList<(TEnum Value, string Name)> Get<TEnum>()
        where TEnum : struct, Enum
    {
        var type = typeof(TEnum);
        var values = Enum.GetValues<TEnum>();
        var result = new List<(TEnum, string)>(values.Length);

        foreach (var value in values)
        {
            var field = type.GetField(value.ToString(), BindingFlags.Public | BindingFlags.Static);
            var name = field?.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name
                ?? field?.GetCustomAttribute<EnumMemberAttribute>()?.Value
                ?? value.ToString();
            result.Add((value, name));
        }

        return result;
    }
}
