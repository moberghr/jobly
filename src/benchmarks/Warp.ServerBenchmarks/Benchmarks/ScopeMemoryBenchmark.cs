using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Warp.Core;
using Warp.Core.Handlers;
using Warp.ServerBenchmarks.Infrastructure;
using JobEntity = Warp.Core.Entities.Job;

namespace Warp.ServerBenchmarks.Benchmarks;

/// <summary>
/// Measures the allocation cost of the scope-create-resolve-dispose pattern
/// that all background tasks and workers use. This is the fundamental operation
/// that could leak if scopes are not properly disposed.
/// </summary>
[Config(typeof(ComponentBenchmarkConfig))]
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "BenchmarkDotNet manages lifecycle via [GlobalCleanup].")]
public class ScopeMemoryBenchmark
{
    private PostgresServerFixture _fixture = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _fixture = new PostgresServerFixture();
        await _fixture.InitializeWithoutHostedServicesAsync();
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _fixture.DisposeAsync();
    }

    /// <summary>
    /// Creates a DI scope, resolves TestContext, disposes the scope.
    /// This is the core pattern of every background task iteration and worker cycle.
    /// </summary>
    [Benchmark(Baseline = true)]
    public void CreateAndDisposeScope()
    {
        using var scope = _fixture.Host.Services.CreateScope();
        _ = scope.ServiceProvider.GetRequiredService<TestContext>();
    }

    /// <summary>
    /// Creates a DI scope, resolves TestContext, runs a simple query, disposes.
    /// Measures the additional allocation cost of executing a query within a scope.
    /// </summary>
    [Benchmark]
    public async Task CreateScopeAndQuery()
    {
        await using var scope = _fixture.Host.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestContext>();
        await ctx.Set<JobEntity>().CountAsync();
    }

    /// <summary>
    /// Creates a DI scope, resolves IPublisher, disposes.
    /// Publisher resolution involves more DI wiring than a plain DbContext.
    /// </summary>
    [Benchmark]
    public void CreateScopeAndResolvePublisher()
    {
        using var scope = _fixture.Host.Services.CreateScope();
        _ = scope.ServiceProvider.GetRequiredService<IPublisher>();
    }
}
