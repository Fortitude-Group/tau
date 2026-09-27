using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tau.Bench;

/// <summary>One bench run: one provider, in-process or HTTP. Serialised as <c>latency-*.json</c>.</summary>
internal sealed record BenchReport(
    string Report,
    string Kind,
    string Provider,
    string ActualProvider,
    string? Tag,
    RunHeader Header,
    WorkloadInfo Workload,
    RunSettings Settings,
    IReadOnlyList<ModelInfo> Models,
    IReadOnlyList<Measurement> Measurements);

internal sealed record RunHeader(
    string Command,
    string? InvokedBy,
    string DateUtc,
    string GitCommit,
    bool? GitDirty,
    string ContractVersion,
    Hardware Hardware,
    Software Software,
    LoadSample LoadBefore,
    LoadSample LoadAfter,
    ProviderCheck ProviderCheck,
    IReadOnlyList<string> Notes);

internal sealed record Hardware(
    string Gpu, string? GpuVram, string? GpuDriver, string Cpu, int? PhysicalCores, int LogicalCores, string Ram, string Os);

internal sealed record Software(string DotNet, string OrtManaged, string? OrtNative, string TauVersion, string ProcessArchitecture);

/// <summary>Machine load sampled just before (or just after) the measured passes.</summary>
internal sealed record LoadSample(
    double? CpuPercent, double? GpuPercent, double? GpuMemoryUsedMiB, int Samples, string Method);

/// <summary>How the run established that the numbers belong to the provider in the file name.</summary>
internal sealed record ProviderCheck(string Requested, string Actual, bool Passed, IReadOnlyList<string> Evidence);

internal sealed record WorkloadInfo(
    string Name, string Description, string Sha256, int StateWords, IReadOnlyDictionary<string, IReadOnlyList<string>> QuestionTypes);

internal sealed record RunSettings(
    int Warmup, int Iterations, int Repeats, IReadOnlyList<int> Questions, string Timer, string PercentileMethod,
    IReadOnlyList<string> Session);

internal sealed record ModelInfo(
    string Id, string Family, string Revision, string OnnxSha256, string Precision, string? ReferencePackage);

/// <summary>One measured quantity (e.g. engine end to end) across every model and question count.</summary>
internal sealed record Measurement(string Id, string Title, string What, IReadOnlyList<Cell> Cells);

/// <summary>One model at one question count: a summary per repeat and the run-to-run change of the p50.</summary>
internal sealed record Cell(
    string Model, int Questions, int BatchRows, int InputTokens, IReadOnlyList<Summary> Repeats, double? P50RepeatDiffPercent);

/// <summary>The merged report written by <c>--combine</c>.</summary>
internal sealed record CombinedReport(string Report, string Command, IReadOnlyList<string> Sources, IReadOnlyList<BenchReport> Runs);

internal static class ReportJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        NewLine = "\n",
    };

    public const string ContractVersion = "systemone/2026-09-27";

    public static void Write<T>(string path, T value) =>
        File.WriteAllText(path, JsonSerializer.Serialize(value, Options) + "\n");

    public static T Read<T>(string path) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? throw new InvalidDataException($"{path}: empty");
}
