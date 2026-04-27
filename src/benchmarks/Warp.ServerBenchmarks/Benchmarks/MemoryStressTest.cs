using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Warp.Core;
using Warp.Core.Handlers;
using Warp.ServerBenchmarks.Infrastructure;

namespace Warp.ServerBenchmarks.Benchmarks;

/// <summary>
/// Standalone stress test that bypasses BenchmarkDotNet.
/// Publishes jobs in rounds, measuring heap retention after each round.
/// Run via: dotnet run --project benchmarks/Warp.ServerBenchmarks -- stress
/// </summary>
public static class MemoryStressTest
{
    public static async Task RunAsync(int workerCount = 10, int jobsPerRound = 10_000, int rounds = 10, bool useDispatcher = false)
    {
        var totalJobs = jobsPerRound * rounds;
        Console.WriteLine($"=== Warp Memory Stress Test ===");
        Console.WriteLine($"Workers: {workerCount}, Jobs per round: {jobsPerRound:N0}, Rounds: {rounds}, Total: {totalJobs:N0}, Dispatcher: {useDispatcher}");
        Console.WriteLine();

        var fixture = new PostgresServerFixture();
        Console.Write("Starting PostgreSQL container... ");
        await fixture.InitializeAsync(workerCount, useDispatcher);
        Console.WriteLine("ready.");

        // Warmup
        Console.Write("Warmup (100 jobs)... ");
        var publisher = fixture.CreatePublisher();
        for (var i = 0; i < 100; i++)
        {
            await publisher.Enqueue(new EmptyRequest());
        }

        await publisher.SaveChangesAsync();
        await fixture.WaitForCompletion();
        await fixture.CleanJobTables();
        Console.WriteLine("done.");
        Console.WriteLine();

        // Force GC and take baseline
        var baselineHeap = ForceGcAndMeasure();
        Console.WriteLine($"{"Round",-8} {"Jobs",-12} {"Time",-10} {"Heap (MB)",-12} {"Delta (MB)",-12} {"Retained (MB)",-15} {"Jobs/sec",-10}");
        Console.WriteLine(new string('-', 90));
        Console.WriteLine($"{"base",-8} {"0",-12} {"-",-10} {baselineHeap / 1024.0 / 1024.0,-12:F1} {"-",-12} {"-",-15} {"-",-10}");

        var prevHeap = baselineHeap;
        var totalProcessed = 0;

        for (var round = 1; round <= rounds; round++)
        {
            var sw = Stopwatch.StartNew();

            // Publish in batches of 1000
            var remaining = jobsPerRound;
            while (remaining > 0)
            {
                var pub = fixture.CreatePublisher();
                var batch = Math.Min(1000, remaining);
                for (var i = 0; i < batch; i++)
                {
                    await pub.Enqueue(new EmptyRequest());
                }

                await pub.SaveChangesAsync();
                remaining -= batch;
            }

            await fixture.WaitForCompletion();
            sw.Stop();

            totalProcessed += jobsPerRound;

            // Clean job tables before GC measurement so DB-related caches don't skew results
            await fixture.CleanJobTables();

            var currentHeap = ForceGcAndMeasure();
            var delta = currentHeap - prevHeap;
            var retained = currentHeap - baselineHeap;
            var jobsPerSec = jobsPerRound / sw.Elapsed.TotalSeconds;

            Console.WriteLine($"{round,-8} {totalProcessed,-12:N0} {sw.Elapsed.TotalSeconds,-10:F1}s {currentHeap / 1024.0 / 1024.0,-12:F1} {delta / 1024.0 / 1024.0,-12:+0.0;-0.0;0.0} {retained / 1024.0 / 1024.0,-15:+0.0;-0.0;0.0} {jobsPerSec,-10:F0}");

            prevHeap = currentHeap;
        }

        Console.WriteLine(new string('-', 90));
        var finalHeap = ForceGcAndMeasure();
        var totalRetained = finalHeap - baselineHeap;
        Console.WriteLine();
        Console.WriteLine($"Baseline heap:    {baselineHeap / 1024.0 / 1024.0:F1} MB");
        Console.WriteLine($"Final heap:       {finalHeap / 1024.0 / 1024.0:F1} MB");
        Console.WriteLine($"Total retained:   {totalRetained / 1024.0 / 1024.0:+0.0;-0.0;0.0} MB");
        Console.WriteLine($"Total jobs:       {totalProcessed:N0}");
        Console.WriteLine($"Per-job retained: {(double)totalRetained / totalProcessed / 1024.0:F2} KB");
        Console.WriteLine();

        if (Math.Abs(totalRetained) < 5 * 1024 * 1024)
        {
            Console.WriteLine("RESULT: No memory leak detected. Heap stable within 5 MB after {0:N0} jobs.", totalProcessed);
        }
        else
        {
            Console.WriteLine("WARNING: Heap grew by {0:F1} MB after {1:N0} jobs. Possible memory leak.", totalRetained / 1024.0 / 1024.0, totalProcessed);
        }

        Console.WriteLine();
        await fixture.DisposeAsync();
    }

    private static long ForceGcAndMeasure()
    {
        GC.Collect(2, GCCollectionMode.Aggressive, true, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Aggressive, true, true);

        return GC.GetTotalMemory(true);
    }
}
