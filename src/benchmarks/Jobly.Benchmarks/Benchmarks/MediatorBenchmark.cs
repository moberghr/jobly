using System;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Jobly.Benchmarks.JoblyLib;
using Jobly.Benchmarks.MediatRLib;
using Jobly.Benchmarks.SourceGenMediator;
using Jobly.Core.Handlers.Generated;
using Microsoft.Extensions.DependencyInjection;

namespace Jobly.Benchmarks.Benchmarks;

[MemoryDiagnoser]
public class MediatorBenchmark
{
    private IServiceScope _joblyScope = null!;
    private IServiceScope _joblySourceGenScope = null!;
    private IServiceScope _mediatrScope = null!;
    private IServiceScope _sourceGenScope = null!;

    private Jobly.Core.Handlers.IMediator _joblyMediator = null!;
    private Jobly.Core.Handlers.IMediator _joblySourceGenMediator = null!;
    private MediatR.IMediator _mediatrMediator = null!;
    private Mediator.IMediator _sourceGenMediator = null!;

    [Params(0, 1, 5)]
    public int PipelineDepth { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Jobly
        var joblyServices = new ServiceCollection();
        joblyServices.AddTransient<Jobly.Core.Handlers.IRequestHandler<JoblyPingRequest, JoblyPingResponse>, JoblyPingHandler>();
        RegisterJoblyBehaviors(joblyServices, PipelineDepth);
        joblyServices.AddScoped<Jobly.Core.Handlers.IMediator>(x => new Jobly.Core.Handlers.Mediator(x));
        var joblyProvider = joblyServices.BuildServiceProvider();
        _joblyScope = joblyProvider.CreateScope();
        _joblyMediator = _joblyScope.ServiceProvider.GetRequiredService<Jobly.Core.Handlers.IMediator>();

        // Jobly (source-generated)
        var joblySourceGenServices = new ServiceCollection();
        joblySourceGenServices.AddJoblyMediator();
        RegisterJoblyBehaviors(joblySourceGenServices, PipelineDepth);
        var joblySourceGenProvider = joblySourceGenServices.BuildServiceProvider();
        _joblySourceGenScope = joblySourceGenProvider.CreateScope();
        _joblySourceGenMediator = _joblySourceGenScope.ServiceProvider.GetRequiredService<Jobly.Core.Handlers.IMediator>();

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
        _joblyScope.Dispose();
        _joblySourceGenScope.Dispose();
        _mediatrScope.Dispose();
        _sourceGenScope.Dispose();
    }

    [Benchmark(Baseline = true)]
    public Task<JoblyPingResponse> Jobly()
        => _joblyMediator.Send(JoblyPingRequest.Instance);

    [Benchmark]
    public Task<JoblyPingResponse> Jobly_SourceGen()
        => _joblySourceGenMediator.Send(JoblyPingRequest.Instance);

    [Benchmark]
    public Task<MediatRPingResponse> MediatR()
        => _mediatrMediator.Send(MediatRPingRequest.Instance);

    [Benchmark]
    public ValueTask<SourceGenPingResponse> Mediator_SourceGen()
        => _sourceGenMediator.Send(SourceGenPingRequest.Instance);

    private static void RegisterJoblyBehaviors(IServiceCollection services, int depth)
    {
        if (depth >= 1)
            services.AddTransient<Jobly.Core.Handlers.IPipelineBehavior<JoblyPingRequest, JoblyPingResponse>, JoblyPassthroughBehavior1>();
        if (depth >= 2)
            services.AddTransient<Jobly.Core.Handlers.IPipelineBehavior<JoblyPingRequest, JoblyPingResponse>, JoblyPassthroughBehavior2>();
        if (depth >= 3)
            services.AddTransient<Jobly.Core.Handlers.IPipelineBehavior<JoblyPingRequest, JoblyPingResponse>, JoblyPassthroughBehavior3>();
        if (depth >= 4)
            services.AddTransient<Jobly.Core.Handlers.IPipelineBehavior<JoblyPingRequest, JoblyPingResponse>, JoblyPassthroughBehavior4>();
        if (depth >= 5)
            services.AddTransient<Jobly.Core.Handlers.IPipelineBehavior<JoblyPingRequest, JoblyPingResponse>, JoblyPassthroughBehavior5>();
    }

    private static void RemoveAllMediatRBehaviors(IServiceCollection services)
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
            services.AddTransient<MediatR.IPipelineBehavior<MediatRPingRequest, MediatRPingResponse>, MediatRPassthroughBehavior1>();
        if (depth >= 2)
            services.AddTransient<MediatR.IPipelineBehavior<MediatRPingRequest, MediatRPingResponse>, MediatRPassthroughBehavior2>();
        if (depth >= 3)
            services.AddTransient<MediatR.IPipelineBehavior<MediatRPingRequest, MediatRPingResponse>, MediatRPassthroughBehavior3>();
        if (depth >= 4)
            services.AddTransient<MediatR.IPipelineBehavior<MediatRPingRequest, MediatRPingResponse>, MediatRPassthroughBehavior4>();
        if (depth >= 5)
            services.AddTransient<MediatR.IPipelineBehavior<MediatRPingRequest, MediatRPingResponse>, MediatRPassthroughBehavior5>();
    }

    private static void RemoveAllSourceGenBehaviors(IServiceCollection services)
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
            services.AddTransient<Mediator.IPipelineBehavior<SourceGenPingRequest, SourceGenPingResponse>, SourceGenPassthroughBehavior1>();
        if (depth >= 2)
            services.AddTransient<Mediator.IPipelineBehavior<SourceGenPingRequest, SourceGenPingResponse>, SourceGenPassthroughBehavior2>();
        if (depth >= 3)
            services.AddTransient<Mediator.IPipelineBehavior<SourceGenPingRequest, SourceGenPingResponse>, SourceGenPassthroughBehavior3>();
        if (depth >= 4)
            services.AddTransient<Mediator.IPipelineBehavior<SourceGenPingRequest, SourceGenPingResponse>, SourceGenPassthroughBehavior4>();
        if (depth >= 5)
            services.AddTransient<Mediator.IPipelineBehavior<SourceGenPingRequest, SourceGenPingResponse>, SourceGenPassthroughBehavior5>();
    }
}
