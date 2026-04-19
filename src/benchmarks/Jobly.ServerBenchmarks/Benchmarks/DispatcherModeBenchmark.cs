using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Attributes;
using Jobly.Core;
using Jobly.Core.Handlers;
using Jobly.ServerBenchmarks.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Jobly.ServerBenchmarks.Benchmarks;

/// <summary>
/// Head-to-head between the two worker modes end-to-end:
/// <list type="bullet">
///   <item><description><c>UseDispatcher = false</c> — each <c>JoblyWorkerService</c> independently fetches + processes + commits per job (pre-dispatcher behaviour).</description></item>
///   <item><description><c>UseDispatcher = true</c> — single <c>JoblyDispatcher</c> batch-fetches jobs and hands them to <c>JoblyDispatcherWorker</c> instances that buffer completions (<c>CompletionBatchSize = 50</c>, default).</description></item>
/// </list>
/// Same workload, same worker count, same handler.
/// </summary>
[Config(typeof(ServerBenchmarkConfig))]
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "BenchmarkDotNet manages lifecycle via [GlobalCleanup].")]
public class DispatcherModeBenchmark
{
    private PostgresServerFixture _fixture = null!;

    [Params(10_000)]
    public int JobCount { get; set; }

    [Params(false, true)]
    public bool UseDispatcher { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        _fixture = new PostgresServerFixture();
        await _fixture.InitializeAsync(workerCount: 10, useDispatcher: UseDispatcher);

        var publisher = _fixture.CreatePublisher();
        for (var i = 0; i < 100; i++)
        {
            await publisher.Enqueue(new EmptyRequest());
        }

        await publisher.SaveChangesAsync();
        await _fixture.WaitForCompletion();
        await _fixture.CleanJobTables();
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _fixture.DisposeAsync();
    }

    [IterationCleanup]
    public void AfterIteration()
    {
        _fixture.CleanJobTables().GetAwaiter().GetResult();
    }

    [Benchmark]
    public async Task ProcessJobs()
    {
        const int batchSize = 1000;
        var remaining = JobCount;
        while (remaining > 0)
        {
            var publisher = _fixture.CreatePublisher();
            var count = Math.Min(batchSize, remaining);
            for (var i = 0; i < count; i++)
            {
                await publisher.Enqueue(new EmptyRequest());
            }

            await publisher.SaveChangesAsync();
            remaining -= count;
        }

        await _fixture.WaitForCompletion();
    }
}
