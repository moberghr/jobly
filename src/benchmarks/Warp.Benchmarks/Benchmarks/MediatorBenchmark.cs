using System;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Warp.Benchmarks.MediatRLib;
using Warp.Benchmarks.SourceGenMediator;
using Warp.Benchmarks.WarpLib;
using Warp.Core.Handlers.Generated;

namespace Warp.Benchmarks.Benchmarks;

[MemoryDiagnoser]
public class MediatorBenchmark
{
    private IServiceScope _warpScope = null!;
    private IServiceScope _warpSourceGenScope = null!;
    private IServiceScope _mediatrScope = null!;
    private IServiceScope _sourceGenScope = null!;

    private Warp.Core.Handlers.IMediator _warpMediator = null!;
    private Warp.Core.Handlers.IMediator _warpSourceGenMediator = null!;
    private MediatR.IMediator _mediatrMediator = null!;
    private Mediator.IMediator _sourceGenMediator = null!;

    [Params(0, 1, 5)]
    public int PipelineDepth { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Warp
        var warpServices = new ServiceCollection();
        warpServices.AddTransient<Warp.Core.Handlers.IRequestHandler<WarpPingRequest, WarpPingResponse>, WarpPingHandler>();
        RegisterWarpBehaviors(warpServices, PipelineDepth);
        warpServices.AddScoped<Warp.Core.Handlers.IMediator>(x => new Warp.Core.Handlers.Mediator(x));
        var warpProvider = warpServices.BuildServiceProvider();
        _warpScope = warpProvider.CreateScope();
        _warpMediator = _warpScope.ServiceProvider.GetRequiredService<Warp.Core.Handlers.IMediator>();

        // Warp (source-generated)
        var warpSourceGenServices = new ServiceCollection();
        warpSourceGenServices.AddWarpMediator();
        RegisterWarpBehaviors(warpSourceGenServices, PipelineDepth);
        var warpSourceGenProvider = warpSourceGenServices.BuildServiceProvider();
        _warpSourceGenScope = warpSourceGenProvider.CreateScope();
        _warpSourceGenMediator = _warpSourceGenScope.ServiceProvider.GetRequiredService<Warp.Core.Handlers.IMediator>();

        // MediatR — assembly scan registers all handlers + behaviors, so remove behaviors then re-add the desired count
        var mediatrServices = new ServiceCollection();
        mediatrServices.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<MediatRPingHandler>());
        RemoveAllMediatRBehaviors(mediatrServices);
        RegisterMediatRBehaviors(mediatrServices, PipelineDepth);
        var mediatrProvider = mediatrServices.BuildServiceProvider();
        _mediatrScope = mediatrProvider.CreateScope();
        _mediatrMediator = _mediatrScope.ServiceProvider.GetRequiredService<MediatR.IMediator>();

        // martinothamar/Mediator (source-generated)
        var sourceGenServices = new ServiceCollection();
        sourceGenServices.AddMediator();
        RemoveAllSourceGenBehaviors(sourceGenServices);
        RegisterSourceGenBehaviors(sourceGenServices, PipelineDepth);
        var sourceGenProvider = sourceGenServices.BuildServiceProvider();
        _sourceGenScope = sourceGenProvider.CreateScope();
        _sourceGenMediator = _sourceGenScope.ServiceProvider.GetRequiredService<Mediator.IMediator>();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _warpScope.Dispose();
        _warpSourceGenScope.Dispose();
        _mediatrScope.Dispose();
        _sourceGenScope.Dispose();
    }

    [Benchmark(Baseline = true)]
    public Task<WarpPingResponse> Warp()
        => _warpMediator.Send(WarpPingRequest.Instance);

    [Benchmark]
    public Task<WarpPingResponse> Warp_SourceGen()
        => _warpSourceGenMediator.Send(WarpPingRequest.Instance);

    [Benchmark]
    public Task<MediatRPingResponse> MediatR()
        => _mediatrMediator.Send(MediatRPingRequest.Instance);

    [Benchmark]
    public ValueTask<SourceGenPingResponse> Mediator_SourceGen()
        => _sourceGenMediator.Send(SourceGenPingRequest.Instance);

    private static void RegisterWarpBehaviors(IServiceCollection services, int depth)
    {
        if (depth >= 1)
        {
            services.AddTransient<Warp.Core.Handlers.IPipelineBehavior<WarpPingRequest, WarpPingResponse>, WarpPassthroughBehavior1>();
        }

        if (depth >= 2)
        {
            services.AddTransient<Warp.Core.Handlers.IPipelineBehavior<WarpPingRequest, WarpPingResponse>, WarpPassthroughBehavior2>();
        }

        if (depth >= 3)
        {
            services.AddTransient<Warp.Core.Handlers.IPipelineBehavior<WarpPingRequest, WarpPingResponse>, WarpPassthroughBehavior3>();
        }

        if (depth >= 4)
        {
            services.AddTransient<Warp.Core.Handlers.IPipelineBehavior<WarpPingRequest, WarpPingResponse>, WarpPassthroughBehavior4>();
        }

        if (depth >= 5)
        {
            services.AddTransient<Warp.Core.Handlers.IPipelineBehavior<WarpPingRequest, WarpPingResponse>, WarpPassthroughBehavior5>();
        }
    }

    private static void RemoveAllMediatRBehaviors(ServiceCollection services)
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType.IsGenericType &&
                services[i].ServiceType.GetGenericTypeDefinition() == typeof(MediatR.IPipelineBehavior<,>))
            {
                services.RemoveAt(i);
            }
        }
    }

    private static void RegisterMediatRBehaviors(IServiceCollection services, int depth)
    {
        if (depth >= 1)
        {
            services.AddTransient<MediatR.IPipelineBehavior<MediatRPingRequest, MediatRPingResponse>, MediatRPassthroughBehavior1>();
        }

        if (depth >= 2)
        {
            services.AddTransient<MediatR.IPipelineBehavior<MediatRPingRequest, MediatRPingResponse>, MediatRPassthroughBehavior2>();
        }

        if (depth >= 3)
        {
            services.AddTransient<MediatR.IPipelineBehavior<MediatRPingRequest, MediatRPingResponse>, MediatRPassthroughBehavior3>();
        }

        if (depth >= 4)
        {
            services.AddTransient<MediatR.IPipelineBehavior<MediatRPingRequest, MediatRPingResponse>, MediatRPassthroughBehavior4>();
        }

        if (depth >= 5)
        {
            services.AddTransient<MediatR.IPipelineBehavior<MediatRPingRequest, MediatRPingResponse>, MediatRPassthroughBehavior5>();
        }
    }

    private static void RemoveAllSourceGenBehaviors(ServiceCollection services)
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType.IsGenericType &&
                services[i].ServiceType.GetGenericTypeDefinition() == typeof(Mediator.IPipelineBehavior<,>))
            {
                services.RemoveAt(i);
            }
        }
    }

    private static void RegisterSourceGenBehaviors(IServiceCollection services, int depth)
    {
        if (depth >= 1)
        {
            services.AddTransient<Mediator.IPipelineBehavior<SourceGenPingRequest, SourceGenPingResponse>, SourceGenPassthroughBehavior1>();
        }

        if (depth >= 2)
        {
            services.AddTransient<Mediator.IPipelineBehavior<SourceGenPingRequest, SourceGenPingResponse>, SourceGenPassthroughBehavior2>();
        }

        if (depth >= 3)
        {
            services.AddTransient<Mediator.IPipelineBehavior<SourceGenPingRequest, SourceGenPingResponse>, SourceGenPassthroughBehavior3>();
        }

        if (depth >= 4)
        {
            services.AddTransient<Mediator.IPipelineBehavior<SourceGenPingRequest, SourceGenPingResponse>, SourceGenPassthroughBehavior4>();
        }

        if (depth >= 5)
        {
            services.AddTransient<Mediator.IPipelineBehavior<SourceGenPingRequest, SourceGenPingResponse>, SourceGenPassthroughBehavior5>();
        }
    }
}
