using Warp.Core.Handlers;

namespace Warp.Tests.TestData.Handlers;

public class CounterCommand : IJobHandler<CounterRequest>
{
    public CounterCommand(CounterService counterService)
    {
        _counterService = counterService;
    }

    private readonly CounterService _counterService;

    public Task HandleAsync(CounterRequest message, CancellationToken cancellationToken)
    {
        _counterService.Increment();
        return Task.CompletedTask;
    }
}

public class CounterRequest : IJob;

public class CounterService
{
    private int _counter;

    public int Counter => _counter;

    public void Increment()
    {
        Interlocked.Increment(ref _counter);
    }
}
