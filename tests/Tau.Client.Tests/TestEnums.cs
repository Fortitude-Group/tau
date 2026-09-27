using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace Tau.Client.Tests;

/// <summary>A plain enum with no wire-name attributes: its wire names are the member names.</summary>
internal enum Urgency
{
    Low,
    Medium,
    High,
}

/// <summary>An enum whose wire names come from <see cref="JsonStringEnumMemberNameAttribute"/>.</summary>
internal enum JsonNamedStatus
{
    [JsonStringEnumMemberName("in-progress")]
    InProgress,

    [JsonStringEnumMemberName("done")]
    Complete,
}

/// <summary>An enum whose wire names come from <see cref="EnumMemberAttribute"/>.</summary>
internal enum LegacyNamedStatus
{
    [EnumMember(Value = "legacy-open")]
    Open,

    [EnumMember(Value = "legacy-closed")]
    Closed,
}
