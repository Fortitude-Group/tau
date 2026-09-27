using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace Tau.Bench;

/// <summary>Hardware, software, git and machine-load facts for the report header. Every probe tolerates a missing tool.</summary>
internal static class HostInfo
{
    public static Hardware Hardware()
    {
        string gpu = "no NVIDIA GPU detected (nvidia-smi unavailable)";
        string? vram = null, driver = null;
        if (Run("nvidia-smi", "--query-gpu=name,memory.total,driver_version --format=csv,noheader") is { } smi)
        {
            var first = smi.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0].Split(',', StringSplitOptions.TrimEntries);
            gpu = first[0];
            vram = first.Length > 1 ? first[1] : null;
            driver = first.Length > 2 ? first[2] : null;
        }

        string cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "unknown CPU";
        int? cores = null;
        string ram = "unknown", os = RuntimeInformation.OSDescription;
        if (OperatingSystem.IsWindows())
        {
            const string ps = "$p = @(Get-CimInstance Win32_Processor); $c = Get-CimInstance Win32_ComputerSystem; "
                + "$o = Get-CimInstance Win32_OperatingSystem; "
                + "[pscustomobject]@{ name = $p[0].Name.Trim(); cores = ($p | Measure-Object NumberOfCores -Sum).Sum; "
                + "ram = $c.TotalPhysicalMemory; os = $o.Caption; ver = $o.Version } | ConvertTo-Json -Compress";
            if (Run("powershell.exe", $"-NoProfile -NonInteractive -Command \"{ps}\"") is { } json
                && JsonNode.Parse(json) is JsonObject o)
            {
                cpu = (string?)o["name"] ?? cpu;
                cores = (int?)o["cores"];
                if ((long?)o["ram"] is { } bytes) ram = $"{bytes / (1024.0 * 1024 * 1024):0.0} GiB";
                os = $"{(string?)o["os"]} {(string?)o["ver"]}".Trim();
            }
        }
        else if (File.Exists("/proc/cpuinfo"))
        {
            var lines = File.ReadAllLines("/proc/cpuinfo");
            cpu = lines.FirstOrDefault(l => l.StartsWith("model name", StringComparison.Ordinal))?.Split(':', 2)[1].Trim() ?? cpu;
            cores = lines.Where(l => l.StartsWith("core id", StringComparison.Ordinal)).Distinct().Count() is > 0 and var c ? c : null;
            var mem = File.Exists("/proc/meminfo") ? File.ReadLines("/proc/meminfo").FirstOrDefault(l => l.StartsWith("MemTotal", StringComparison.Ordinal)) : null;
            if (mem is not null && long.TryParse(mem.Split(':')[1].Trim().Split(' ')[0], out var kb)) ram = $"{kb / (1024.0 * 1024):0.0} GiB";
        }

        return new Hardware(gpu, vram, driver, cpu, cores, Environment.ProcessorCount, ram, os);
    }

    public static Software Software(string? ortNative)
    {
        var ortAsm = typeof(Microsoft.ML.OnnxRuntime.InferenceSession).Assembly;
        var ortManaged = ortAsm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0]
                         ?? ortAsm.GetName().Version?.ToString() ?? "unknown";
        var tau = typeof(Tau.Inference.Engine.OnnxDecisionEngine).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "unknown";
        return new Software(RuntimeInformation.FrameworkDescription, ortManaged, ortNative, tau,
            RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant());
    }

    /// <summary>HEAD commit and whether the working tree has uncommitted changes (null when git can't tell).</summary>
    public static (string Commit, bool? Dirty) Git(string repoRoot)
    {
        var head = Run("git", $"-C \"{repoRoot}\" rev-parse HEAD");
        if (head is null) return ("unknown (git not available)", null);
        var status = Run("git", $"-C \"{repoRoot}\" status --porcelain --untracked-files=no", allowEmpty: true);
        return (head.Trim(), status is null ? null : status.Trim().Length > 0);
    }

    /// <summary>
    /// Samples system CPU utilisation and GPU utilisation/memory over about 1.5 s. It's a snapshot, recorded so a reader
    /// can see the machine wasn't idle-perfect, not a guarantee about the whole run.
    /// </summary>
    public static LoadSample SampleLoad()
    {
        var cpuStart = CpuTimes();
        var gpuUtil = new List<double>();
        var gpuMem = new List<double>();
        const int samples = 3;
        for (var i = 0; i < samples; i++)
        {
            if (Run("nvidia-smi", "--query-gpu=utilization.gpu,memory.used --format=csv,noheader,nounits") is { } line)
            {
                var parts = line.Split('\n')[0].Split(',', StringSplitOptions.TrimEntries);
                if (parts.Length >= 2
                    && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var u)
                    && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var m))
                {
                    gpuUtil.Add(u);
                    gpuMem.Add(m);
                }
            }
            Thread.Sleep(500);
        }
        var cpuEnd = CpuTimes();
        double? cpu = null;
        if (cpuStart is { } a && cpuEnd is { } b && b.Total > a.Total)
            cpu = Stats.R(100.0 * (1.0 - (double)(b.Idle - a.Idle) / (b.Total - a.Total)));
        var method = (OperatingSystem.IsWindows() ? "CPU: GetSystemTimes busy share" : "CPU: /proc/stat busy share")
                     + $" over the sampling window; GPU: mean of {samples} nvidia-smi utilization.gpu/memory.used readings 0.5 s apart";
        return new LoadSample(cpu, gpuUtil.Count > 0 ? Stats.R(gpuUtil.Average()) : null,
            gpuMem.Count > 0 ? Stats.R(gpuMem.Average()) : null, samples, method);
    }

    /// <summary>
    /// Whether nvidia-smi lists this process as a compute client (null when nvidia-smi can't answer). A CUDA session
    /// that really runs on the GPU holds a CUDA context, so its process id shows up here.
    /// </summary>
    public static bool? ProcessOnGpu()
    {
        var output = Run("nvidia-smi", "--query-compute-apps=pid --format=csv,noheader", allowEmpty: true);
        if (output is null) return null;
        var pid = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(pid);
    }

    private readonly record struct Times(ulong Idle, ulong Total);

    private static Times? CpuTimes()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                if (!GetSystemTimes(out var idle, out var kernel, out var user)) return null;
                return new Times(idle, kernel + user); // kernel time includes idle time
            }
            if (File.Exists("/proc/stat"))
            {
                var f = File.ReadLines("/proc/stat").First().Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1)
                    .Select(ulong.Parse).ToArray();
                return new Times(f[3] + (f.Length > 4 ? f[4] : 0), f.Aggregate(0UL, (s, x) => s + x));
            }
        }
        catch (Exception e) when (e is IOException or FormatException)
        {
        }
        return null;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
#pragma warning disable SYSLIB1054 // LibraryImport would need unsafe code enabled for this one call
    private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
#pragma warning restore SYSLIB1054

    public static string? Run(string file, string args, bool allowEmpty = false)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args)
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            var errTask = p.StandardError.ReadToEndAsync();
            var output = p.StandardOutput.ReadToEnd().Trim().Replace("\r", "", StringComparison.Ordinal);
            if (!p.WaitForExit(30_000)) return null;
            _ = errTask.Result;
            if (p.ExitCode != 0) return null;
            return output.Length == 0 && !allowEmpty ? null : output;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return null;
        }
    }
}
