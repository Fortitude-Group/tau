using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Tau.Workbench.Report;

/// <summary>The machine a report was produced on.</summary>
/// <param name="Gpu">GPU name, or a statement that none was detected.</param>
/// <param name="GpuDriver">GPU driver version, when known.</param>
/// <param name="Cpu">CPU name.</param>
/// <param name="LogicalCores">Logical processors.</param>
/// <param name="Os">Operating system.</param>
/// <param name="Runtime">.NET runtime.</param>
public sealed record HardwareInfo(string Gpu, string? GpuDriver, string Cpu, int LogicalCores, string Os, string Runtime);

/// <summary>The git state of the repository when the report was produced.</summary>
/// <param name="Commit">HEAD, or a statement that git is unavailable.</param>
/// <param name="Dirty">Whether tracked files outside the run's outputs differ from HEAD (null when unknown).</param>
public sealed record GitInfo(string Commit, bool? Dirty);

/// <summary>Probes hardware and git. Every probe tolerates a missing tool.</summary>
public static class HostProbe
{
    /// <summary>Reads hardware facts (nvidia-smi for the GPU; the registry or /proc/cpuinfo for the CPU).</summary>
    public static HardwareInfo Hardware()
    {
        string gpu = "no NVIDIA GPU detected (nvidia-smi unavailable)";
        string? driver = null;
        if (Run("nvidia-smi", ["--query-gpu=name,driver_version", "--format=csv,noheader"]) is { } smi)
        {
            var first = smi.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0].Split(',', StringSplitOptions.TrimEntries);
            gpu = first[0];
            driver = first.Length > 1 ? first[1] : null;
        }

        string cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "unknown CPU";
        if (OperatingSystem.IsWindows())
        {
            cpu = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", null) is string name
                ? name.Trim() : cpu;
        }
        else if (File.Exists("/proc/cpuinfo"))
        {
            cpu = File.ReadLines("/proc/cpuinfo").FirstOrDefault(l => l.StartsWith("model name", StringComparison.Ordinal))?.Split(':', 2)[1].Trim() ?? cpu;
        }

        return new HardwareInfo(gpu, driver, cpu, Environment.ProcessorCount, RuntimeInformation.OSDescription, RuntimeInformation.FrameworkDescription);
    }

    /// <summary>
    /// Reads HEAD and whether tracked files differ from it, ignoring the run's own outputs under the spec
    /// directory (runs, calibrators, frontier, threshold, cascade and report files), which the run itself rewrites.
    /// </summary>
    /// <param name="repoRoot">The repository root.</param>
    /// <param name="specDirectory">The spec's directory.</param>
    public static GitInfo Git(string repoRoot, string specDirectory)
    {
        var head = Run("git", ["-C", repoRoot, "rev-parse", "HEAD"]);
        if (head is null)
        {
            return new GitInfo("unknown (git not available)", null);
        }

        var rel = Path.GetRelativePath(repoRoot, specDirectory).Replace('\\', '/');
        var args = new List<string> { "-C", repoRoot, "status", "--porcelain", "--untracked-files=no", "--", "." };
        foreach (var output in new[] { "runs", "calibrators", "frontier", "threshold.json", "cascade.json", "report.json", "report.html" })
        {
            args.Add($":(exclude){(rel == "." ? "" : rel + "/")}{output}");
        }

        var status = Run("git", args, allowEmpty: true);
        return new GitInfo(head.Trim(), status is null ? null : status.Trim().Length > 0);
    }

    private static string? Run(string file, IEnumerable<string> args, bool allowEmpty = false)
    {
        try
        {
            var psi = new ProcessStartInfo(file)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in args)
            {
                psi.ArgumentList.Add(a);
            }

            using var p = Process.Start(psi);
            if (p is null)
            {
                return null;
            }

            var output = p.StandardOutput.ReadToEndAsync();
            _ = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(30_000))
            {
                p.Kill(entireProcessTree: true); // a process this probe started
                return null;
            }

            var text = output.Result.Trim().Replace("\r", "", StringComparison.Ordinal);
            return p.ExitCode != 0 || (text.Length == 0 && !allowEmpty) ? null : text;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }
}
