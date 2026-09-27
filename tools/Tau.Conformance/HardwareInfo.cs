using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Tau.Conformance;

/// <summary>The hardware fingerprint every committed report must carry (constitution Principle XIII).</summary>
internal sealed record HardwareFingerprint(string Gpu, string Cpu, string Ram, string Os);

/// <summary>Reads the hardware fingerprint from the running machine, tolerating any tool being absent.</summary>
internal static class HardwareInfo
{
    public static HardwareFingerprint Capture() => new(Gpu(), Cpu(), Ram(), RuntimeInformation.OSDescription);

    private static string Gpu()
    {
        var output = RunCommand("nvidia-smi", "--query-gpu=name,memory.total,driver_version --format=csv,noheader");
        return output is null || output.Length == 0 ? "no NVIDIA GPU detected (nvidia-smi unavailable)" : output;
    }

    private static string Cpu()
    {
        var id = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
        if (!string.IsNullOrWhiteSpace(id))
        {
            return id;
        }

        try
        {
            if (File.Exists("/proc/cpuinfo"))
            {
                var line = File.ReadLines("/proc/cpuinfo").FirstOrDefault(l => l.StartsWith("model name", StringComparison.Ordinal));
                if (line is not null)
                {
                    return line.Split(':', 2)[1].Trim();
                }
            }
        }
        catch (IOException)
        {
            // best effort only
        }

        return "unknown CPU";
    }

    private static string Ram()
    {
        try
        {
            var bytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            return bytes > 0
                ? $"{bytes / (1024.0 * 1024 * 1024):0.#} GB (reported by the .NET GC as total available memory)"
                : "unknown RAM";
        }
        catch (PlatformNotSupportedException)
        {
            return "unknown RAM";
        }
    }

    private static string? RunCommand(string fileName, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);
            return output.Length == 0 ? null : output;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            return null;
        }
    }
}
