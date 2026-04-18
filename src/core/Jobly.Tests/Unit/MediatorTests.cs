using Jobly.Core.Handlers;
using Jobly.Tests.TestData.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobly.Tests.Unit;

[Trait("Category", "NoDb")]
public class MediatorTests
{
    [TimedFact]
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

    [TimedFact]
    public async Task Send_WithNoHandler_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        services.AddScoped<IMediator>(x => new Mediator(x));
        var provider = services.BuildServiceProvider();

        var mediator = provider.GetRequiredService<IMediator>();

        await Should.ThrowAsync<InvalidOperationException>(
            () => mediator.Send(new GetGreetingRequest { Name = "Jobly" }));
    }

    [TimedFact]
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

    [TimedFact]
    public async Task CreateStream_WithStreamHandler_ReturnsItems()
    {
        var services = new ServiceCollection();
        services.AddTransient<IStreamRequestHandler<GetNumbersStreamRequest, int>, GetNumbersStreamHandler>();
        services.AddScoped<IMediator>(x => new Mediator(x));
        var provider = services.BuildServiceProvider();

        var mediator = provider.GetRequiredService<IMediator>();
        var items = new List<int>();
        await foreach (var item in mediator.CreateStream(new GetNumbersStreamRequest { Count = 5 }))
        {
            items.Add(item);
        }

        items.ShouldBe([0, 1, 2, 3, 4]);
    }

    [TimedFact]
    public void CreateStream_WithNoHandler_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        services.AddScoped<IMediator>(x => new Mediator(x));
        var provider = services.BuildServiceProvider();

        var mediator = provider.GetRequiredService<IMediator>();

        Should.Throw<InvalidOperationException>(
            () => mediator.CreateStream(new GetNumbersStreamRequest { Count = 1 }));
    }

    [TimedFact]
    public async Task CreateStream_WithPipelineBehavior_ExecutesPipeline()
    {
        var log = new List<string>();

        var services = new ServiceCollection();
        services.AddTransient<IStreamRequestHandler<GetNumbersStreamRequest, int>, GetNumbersStreamHandler>();
        services.AddTransient<IStreamPipelineBehavior<GetNumbersStreamRequest, int>>(
            _ => new StreamTrackingBehavior<GetNumbersStreamRequest, int>(log));
        services.AddScoped<IMediator>(x => new Mediator(x));
        var provider = services.BuildServiceProvider();

        var mediator = provider.GetRequiredService<IMediator>();
        var items = new List<int>();
        await foreach (var item in mediator.CreateStream(new GetNumbersStreamRequest { Count = 3 }))
        {
            items.Add(item);
        }

        items.ShouldBe([0, 1, 2]);
        log.ShouldBe(["before", "after"]);
    }

    [TimedFact]
    public async Task CreateStream_WithCancellation_StopsEnumeration()
    {
        var services = new ServiceCollection();
        services.AddTransient<IStreamRequestHandler<GetNumbersStreamRequest, int>, GetNumbersStreamHandler>();
        services.AddScoped<IMediator>(x => new Mediator(x));
        var provider = services.BuildServiceProvider();

        var mediator = provider.GetRequiredService<IMediator>();
        var items = new List<int>();
        using var cts = new CancellationTokenSource();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            await foreach (var item in mediator.CreateStream(new GetNumbersStreamRequest { Count = 100 }, cts.Token))
            {
                items.Add(item);
                if (items.Count == 3)
                {
                    await cts.CancelAsync();
                }
            }
        });

        items.Count.ShouldBe(3);
    }

    [TimedFact]
    public async Task CreateStream_WithEmptyStream_ReturnsNoItems()
    {
        var services = new ServiceCollection();
        services.AddTransient<IStreamRequestHandler<GetNumbersStreamRequest, int>, GetNumbersStreamHandler>();
        services.AddScoped<IMediator>(x => new Mediator(x));
        var provider = services.BuildServiceProvider();

        var mediator = provider.GetRequiredService<IMediator>();
        var items = new List<int>();
        await foreach (var item in mediator.CreateStream(new GetNumbersStreamRequest { Count = 0 }))
        {
            items.Add(item);
        }

        items.ShouldBeEmpty();
    }

    [TimedFact]
    public async Task CreateStream_WithMultipleBehaviors_ExecutesInOrder()
    {
        var log = new List<string>();

        var services = new ServiceCollection();
        services.AddTransient<IStreamRequestHandler<GetNumbersStreamRequest, int>, GetNumbersStreamHandler>();
        services.AddTransient<IStreamPipelineBehavior<GetNumbersStreamRequest, int>>(
            _ => new StreamTrackingBehavior<GetNumbersStreamRequest, int>(log, "outer"));
        services.AddTransient<IStreamPipelineBehavior<GetNumbersStreamRequest, int>>(
            _ => new StreamTrackingBehavior<GetNumbersStreamRequest, int>(log, "inner"));
        services.AddScoped<IMediator>(x => new Mediator(x));
        var provider = services.BuildServiceProvider();

        var mediator = provider.GetRequiredService<IMediator>();
        var items = new List<int>();
        await foreach (var item in mediator.CreateStream(new GetNumbersStreamRequest { Count = 2 }))
        {
            items.Add(item);
        }

        items.ShouldBe([0, 1]);
        log.ShouldBe(["outer:before", "inner:before", "inner:after", "outer:after"]);
    }

    [TimedFact]
    public async Task CreateStream_WithHandlerException_PropagatesException()
    {
        var services = new ServiceCollection();
        services.AddTransient<IStreamRequestHandler<GetNumbersStreamRequest, int>, FailingStreamHandler>();
        services.AddScoped<IMediator>(x => new Mediator(x));
        var provider = services.BuildServiceProvider();

        var mediator = provider.GetRequiredService<IMediator>();
        var items = new List<int>();

        await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await foreach (var item in mediator.CreateStream(new GetNumbersStreamRequest { Count = 5 }))
            {
                items.Add(item);
            }
        });

        items.ShouldBe([0, 1]);
    }

    [TimedFact]
    public async Task CreateStream_WithRequestPipelineBehavior_ExecutesBehavior()
    {
        var log = new List<string>();

        var services = new ServiceCollection();
        services.AddTransient<IStreamRequestHandler<GetNumbersStreamRequest, int>, GetNumbersStreamHandler>();
        services.AddTransient<IPipelineBehavior<GetNumbersStreamRequest, IAsyncEnumerable<int>>>(
            _ => new TrackingBehavior<GetNumbersStreamRequest, IAsyncEnumerable<int>>(log));
        services.AddScoped<IMediator>(x => new Mediator(x));
        var provider = services.BuildServiceProvider();

        var mediator = provider.GetRequiredService<IMediator>();
        var items = new List<int>();
        await foreach (var item in mediator.CreateStream(new GetNumbersStreamRequest { Count = 3 }))
        {
            items.Add(item);
        }

        items.ShouldBe([0, 1, 2]);
        log.ShouldBe(["before", "after"]);
    }

    private class TrackingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        private readonly List<string> _log;

        public TrackingBehavior(List<string> log) => _log = log;

        public async Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TRequest, TResponse> next, CancellationToken ct)
        {
            _log.Add("before");
            var result = await next(request, ct);
            _log.Add("after");

            return result;
        }
    }

    private class StreamTrackingBehavior<TRequest, TResponse> : IStreamPipelineBehavior<TRequest, TResponse>
        where TRequest : IStreamRequest<TResponse>
    {
        private readonly List<string> _log;
        private readonly string _label;

        public StreamTrackingBehavior(List<string> log, string label = "")
        {
            _log = log;
            _label = label;
        }

        public async IAsyncEnumerable<TResponse> HandleAsync(
            TRequest request,
            StreamHandlerDelegate<TRequest, TResponse> next,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            _log.Add(string.IsNullOrEmpty(_label) ? "before" : $"{_label}:before");
            await foreach (var item in next(request, ct).WithCancellation(ct))
            {
                yield return item;
            }

            _log.Add(string.IsNullOrEmpty(_label) ? "after" : $"{_label}:after");
        }
    }

    private class FailingStreamHandler : IStreamRequestHandler<GetNumbersStreamRequest, int>
    {
        public async IAsyncEnumerable<int> HandleAsync(
            GetNumbersStreamRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            yield return 0;
            await Task.Yield();
            yield return 1;
            await Task.Yield();
            throw new InvalidOperationException("Simulated failure");
        }
    }
}
