using System.Runtime.InteropServices;

namespace Warp.Worker.Services;

/// <summary>
/// Per-process, cross-iteration state for CPU % / working-set sampling. Scoped
/// <see cref="Heartbeat{TContext}"/> instances can't hold this themselves because they are
/// disposed between iterations — the <c>_previousCpuTime</c> delta would reset every few
/// seconds and produce near-zero CPU % readings. Registered as a singleton.
/// </summary>
public sealed class ProcessCpuTracker
{
    private TimeSpan? _previousCpuTime;
    private DateTime _previousWallTime;

    public ProcessCpuTracker(TimeProvider time)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            _previousCpuTime = process.TotalProcessorTime;
        }
        catch
        {
            // Process metrics not available in this environment — Sample returns null.
        }

        _previousWallTime = time.GetUtcNow().UtcDateTime;
    }

    public Snapshot? Sample(DateTime now)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            var workingSet = process.WorkingSet64;
            double? cpuPercent = null;

            if (_previousCpuTime.HasValue)
            {
                var currentCpuTime = process.TotalProcessorTime;
                var wallElapsed = (now - _previousWallTime).TotalMilliseconds;
                if (wallElapsed > 0)
                {
                    var cpuElapsed = (currentCpuTime - _previousCpuTime.Value).TotalMilliseconds;
                    cpuPercent = Math.Round(cpuElapsed / wallElapsed / Environment.ProcessorCount * 100, 1);
                }

                _previousCpuTime = currentCpuTime;
                _previousWallTime = now;
            }

            return new Snapshot(workingSet, cpuPercent);
        }
        catch
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Auto)]
    public readonly record struct Snapshot(long WorkingSet, double? CpuPercent);
}
