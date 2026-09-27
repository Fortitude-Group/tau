using System.Diagnostics;

namespace Tau.Conformance;

/// <summary>Reads the current commit so every report is traceable to the code that produced it.</summary>
internal static class GitInfo
{
    public static string CurrentCommit()
    {
        try
        {
            var psi = new ProcessStartInfo("git", "rev-parse HEAD")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (process is null)
            {
                return "unknown (git not available)";
            }

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);
            return output.Length == 0 ? "unknown (git returned no output)" : output;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            return $"unknown ({ex.Message})";
        }
    }
}
