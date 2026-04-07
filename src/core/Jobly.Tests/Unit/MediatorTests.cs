using Jobly.Core.Handlers;
using Jobly.Tests.TestData.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobly.Tests.Unit;

public class MediatorTests
{
    [Fact]
    public async Task Send_WithRequestHandler_ReturnsResponse()
    {
        var services = new ServiceCollection();
        services.AddTransient<IRequestHandler<GetGreetingRequest, string>, GetGreetingHandler>();
        services.AddScoped<IMediator>(x => new Mediator(x));
        var provider = services.BuildServiceProvider();

        var mediator = provider.GetRequiredService<IMediator>();
        var result = await mediator.Send(new GetGreetingRequest { Name = "Jobly" });

        result.ShouldBe("Hello, Jobly!");
    }

    [Fact]
    public async Task Send_WithNoHandler_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        services.AddScoped<IMediator>(x => new Mediator(x));
        var provider = services.BuildServiceProvider();

        var mediator = provider.GetRequiredService<IMediator>();

        await Should.ThrowAsync<InvalidOperationException>(
            () => mediator.Send(new GetGreetingRequest { Name = "Jobly" }));
    }

    [Fact]
    public async Task Send_WithPipelineBehavior_ExecutesPipeline()
    {
        var log = new List<string>();

        var services = new ServiceCollection();
        services.AddTransient<IRequestHandler<GetGreetingRequest, string>, GetGreetingHandler>();
        services.AddTransient<IPipelineBehavior<GetGreetingRequest, string>>(
            _ => new TrackingBehavior<GetGreetingRequest, string>(log));
        services.AddScoped<IMediator>(x => new Mediator(x));
        var provider = services.BuildServiceProvider();

        var mediator = provider.GetRequiredService<IMediator>();
        var result = await mediator.Send(new GetGreetingRequest { Name = "World" });

        result.ShouldBe("Hello, World!");
        log.ShouldBe(["before", "after"]);
    }

    private class TrackingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        private readonly List<string> _log;

        public TrackingBehavior(List<string> log) => _log = log;

        public async Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
        {
            _log.Add("before");
            var result = await next();
            _log.Add("after");
            return result;
        }
    }
}
