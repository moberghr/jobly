using System;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Jobly.Benchmarks.JoblyLib;
using Jobly.Core.Handlers;
using Jobly.Core.Handlers.Generated;
using Microsoft.Extensions.DependencyInjection;

namespace Jobly.Benchmarks.Benchmarks;

[MemoryDiagnoser]
public class WorkerDispatchBenchmark
{
    private IServiceScope _scope = null!;
    private IServiceProvider _scopedProvider = null!;

    private Type _messageType = null!;
    private Type _handlerType = null!;

    [Params(0, 1, 5)]
    public int PipelineDepth { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddTransient<IJobHandler<BenchmarkJob>, BenchmarkJobHandler>();
        if (PipelineDepth >= 1)
            services.AddTransient<IPipelineBehavior<BenchmarkJob, Unit>, JobBehavior1>();
        if (PipelineDepth >= 2)
            services.AddTransient<IPipelineBehavior<BenchmarkJob, Unit>, JobBehavior2>();
        if (PipelineDepth >= 3)
            services.AddTransient<IPipelineBehavior<BenchmarkJob, Unit>, JobBehavior3>();
        if (PipelineDepth >= 4)
            services.AddTransient<IPipelineBehavior<BenchmarkJob, Unit>, JobBehavior4>();
        if (PipelineDepth >= 5)
            services.AddTransient<IPipelineBehavior<BenchmarkJob, Unit>, JobBehavior5>();

        var provider = services.BuildServiceProvider();
        _scope = provider.CreateScope();
        _scopedProvider = _scope.ServiceProvider;

        // Simulate what the worker knows: message type + handler type from DB
        _messageType = typeof(BenchmarkJob);
        _handlerType = typeof(BenchmarkJobHandler);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _scope.Dispose();
    }

    /// <summary>
    /// Current reflection path: JobDispatcher.ExecuteHandler → MakeGenericMethod → MethodInfo.Invoke
    /// </summary>
    [Benchmark(Baseline = true)]
    public Task Worker_Reflection()
    {
        return JobDispatcher.ExecuteHandler(
            BenchmarkJob.Instance,
            _messageType,
            _handlerType,
            _scopedProvider,
            CancellationToken.None);
    }

    /// <summary>
    /// Source-generated path: direct generic calls, no reflection.
    /// Uses the actual generated GeneratedJobDispatcher.
    /// </summary>
    [Benchmark]
    public Task Worker_SourceGen()
    {
        return GeneratedJobDispatcher.TryExecute(
            BenchmarkJob.Instance,
            _messageType,
            _handlerType,
            _scopedProvider,
            CancellationToken.None)!;
    }
}
