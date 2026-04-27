using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace Warp.Worker;

/// <summary>
/// Lightweight per-job profiler. Uses AsyncLocal to track a long[] of timestamps
/// across async continuations. When disabled, Mark() is a single volatile bool check.
/// </summary>
public static class PerfTrace
{
    private static readonly AsyncLocal<long[]?> CurrentTicks = new();
    private static readonly ConcurrentQueue<long[]> Traces = new();
    private static volatile bool _enabled;

    // Step indices — must match the order of Mark() calls
    public const int BeginTransaction1 = 0;
    public const int FetchJob = 1;
    public const int SaveProcessing = 2;
    public const int CommitTransaction1 = 3;
    public const int ExecuteHandler = 4;
    public const int CancelKeepAlive = 5;
    public const int BeginTransaction2 = 6;
    public const int IncrementStats = 7;
    public const int CheckChildren = 8;
    public const int CheckBatchMessage = 9;
    public const int SaveCompleted = 10;
    public const int CommitTransaction2 = 11;
    public const int Done = 12;
    private const int MaxSteps = 13;

    private static readonly string[] StepNames =
    [
        "BeginTransaction1",
        "FetchJob",
        "SaveProcessing",
        "CommitTransaction1",
        "ExecuteHandler",
        "CancelKeepAlive",
        "BeginTransaction2",
        "IncrementStats",
        "CheckChildren",
        "CheckBatchMessage",
        "SaveCompleted",
        "CommitTransaction2",
        "Done",
    ];

    public static bool Enabled => _enabled;

    public static void Enable() => _enabled = true;

    public static void Disable() => _enabled = false;

    public static void Begin()
    {
        if (!_enabled)
        {
            return;
        }

        CurrentTicks.Value = new long[MaxSteps];
    }

    public static void Mark(int step)
    {
        if (!_enabled)
        {
            return;
        }

        var ticks = CurrentTicks.Value;
        if (ticks != null)
        {
            ticks[step] = Stopwatch.GetTimestamp();
        }
    }

    public static long[]? Detach()
    {
        if (!_enabled)
        {
            return null;
        }

        var ticks = CurrentTicks.Value;
        CurrentTicks.Value = null;
        return ticks;
    }

    public static void Attach(long[]? ticks)
    {
        if (!_enabled || ticks == null)
        {
            return;
        }

        CurrentTicks.Value = ticks;
    }

    public static void End()
    {
        if (!_enabled)
        {
            return;
        }

        var ticks = CurrentTicks.Value;
        if (ticks == null)
        {
            return;
        }

        // Find how many steps were actually recorded
        var count = 0;
        for (var i = 0; i < MaxSteps; i++)
        {
            if (ticks[i] != 0)
            {
                count = i + 1;
            }
        }

        if (count < 2)
        {
            CurrentTicks.Value = null;
            return;
        }

        var snapshot = new long[count];
        Array.Copy(ticks, snapshot, count);
        Traces.Enqueue(snapshot);
        CurrentTicks.Value = null;
    }

    public static string Dump()
    {
        var all = new List<long[]>();
        while (Traces.TryDequeue(out var t))
        {
            all.Add(t);
        }

        if (all.Count == 0)
        {
            return "No traces collected.\n";
        }

        var freq = (double)Stopwatch.Frequency / 1000.0; // ticks per ms
        var stepData = new Dictionary<string, List<double>>();
        var totals = new List<double>();

        foreach (var ticks in all)
        {
            double total = 0;
            for (var i = 0; i < ticks.Length - 1; i++)
            {
                if (ticks[i] == 0 || ticks[i + 1] == 0)
                {
                    continue;
                }

                var ms = (ticks[i + 1] - ticks[i]) / freq;
                var name = StepNames[i];

                if (!stepData.TryGetValue(name, out var list))
                {
                    list = [];
                    stepData[name] = list;
                }

                list.Add(ms);
                total += ms;
            }

            totals.Add(total);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Performance Trace Summary ({all.Count} jobs)");
        sb.AppendLine(new string('-', 80));
        sb.AppendLine($"{"Step",-35} {"Avg ms",8} {"P50 ms",8} {"P95 ms",8} {"P99 ms",8} {"Max ms",8}");
        sb.AppendLine(new string('-', 80));

        foreach (var (name, values) in stepData)
        {
            values.Sort();
            sb.AppendLine($"{name,-35} {values.Average(),8:F2} {Pct(values, 0.50),8:F2} {Pct(values, 0.95),8:F2} {Pct(values, 0.99),8:F2} {values[^1],8:F2}");
        }

        totals.Sort();
        sb.AppendLine(new string('-', 80));
        sb.AppendLine($"{"TOTAL",-35} {totals.Average(),8:F2} {Pct(totals, 0.50),8:F2} {Pct(totals, 0.95),8:F2} {Pct(totals, 0.99),8:F2} {totals[^1],8:F2}");
        sb.AppendLine();
        sb.AppendLine($"Throughput: {all.Count} jobs, {totals.Average():F1}ms avg per job");

        return sb.ToString();
    }

    private static double Pct(List<double> sorted, double p)
    {
        var index = (int)Math.Ceiling(p * sorted.Count) - 1;
        return sorted[Math.Max(0, index)];
    }
}
