using System.Diagnostics;
using System.Globalization;

namespace Tau.Workbench.Measure;

/// <summary>
/// Samples whole-GPU power (<c>nvidia-smi --query-gpu=power.draw</c>, first GPU) at a fixed interval while a
/// measurement phase runs. The figure is the board's total draw, including idle and any other process on
/// the GPU, so it overstates the energy of the model alone; reports say so.
/// </summary>
public sealed class GpuPowerSampler : IAsyncDisposable
{
    /// <summary>The note attached to every GPU power figure.</summary>
    public const string WholeGpuNote = "Mean of nvidia-smi power.draw for the first GPU, sampled while the phase ran. It is whole-GPU power: idle draw and any other work on the GPU are included, so it overstates what the model alone used.";

    private readonly Func<CancellationToken, Task<double?>> _probe;
    private readonly List<double> _samples = [];
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    /// <summary>Starts sampling with a custom probe (tests) or nvidia-smi.</summary>
    /// <param name="probe">Returns one power reading in watts, or null when none is available.</param>
    /// <param name="interval">Sampling interval.</param>
    /// <param name="gpuName">GPU name for the record.</param>
    public GpuPowerSampler(Func<CancellationToken, Task<double?>> probe, TimeSpan interval, string? gpuName = null)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        Interval = interval;
        GpuName = gpuName;
        _loop = Task.Run(LoopAsync);
    }

    /// <summary>The sampling interval.</summary>
    public TimeSpan Interval { get; }

    /// <summary>The GPU's name, when known.</summary>
    public string? GpuName { get; }

    /// <summary>
    /// Starts a sampler backed by nvidia-smi every 500 ms, or returns null (with the reason) when
    /// nvidia-smi is not available.
    /// </summary>
    /// <param name="reason">Why no sampler was started.</param>
    public static GpuPowerSampler? TryStartNvidiaSmi(out string? reason)
    {
        var name = RunNvidiaSmi("--query-gpu=name --format=csv,noheader", TimeSpan.FromSeconds(10))?.Split('\n')[0].Trim();
        if (name is null)
        {
            reason = "nvidia-smi is not available, so GPU power was not sampled and no local energy estimate can be made.";
            return null;
        }

        reason = null;
        return new GpuPowerSampler(_ => Task.FromResult(ReadPower()), TimeSpan.FromMilliseconds(500), name);
    }

    /// <summary>Stops sampling and returns the mean, or null when no sample was taken.</summary>
    public async Task<GpuPower?> StopAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        await _loop.ConfigureAwait(false);
        lock (_samples)
        {
            return _samples.Count == 0 ? null
                : new GpuPower(_samples.Average(), _samples.Count, (int)Interval.TotalMilliseconds, GpuName, WholeGpuNote);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (!_stop.IsCancellationRequested)
        {
            await StopAsync().ConfigureAwait(false);
        }

        _stop.Dispose();
    }

    private async Task LoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                if (await _probe(_stop.Token).ConfigureAwait(false) is { } watts && double.IsFinite(watts))
                {
                    lock (_samples)
                    {
                        _samples.Add(watts);
                    }
                }

                var wait = Interval - Stopwatch.GetElapsedTime(started);
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, _stop.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private static double? ReadPower()
    {
        var line = RunNvidiaSmi("--query-gpu=power.draw --format=csv,noheader,nounits", TimeSpan.FromSeconds(5))?.Split('\n')[0].Trim();
        return double.TryParse(line, NumberStyles.Float, CultureInfo.InvariantCulture, out var w) ? w : null;
    }

    private static string? RunNvidiaSmi(string args, TimeSpan timeout)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("nvidia-smi", args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (p is null)
            {
                return null;
            }

            var output = p.StandardOutput.ReadToEndAsync();
            _ = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeout))
            {
                p.Kill(entireProcessTree: true); // only ever a process this sampler started
                return null;
            }

            return p.ExitCode == 0 && output.Result.Length > 0 ? output.Result.Replace("\r", "", StringComparison.Ordinal) : null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }
}
