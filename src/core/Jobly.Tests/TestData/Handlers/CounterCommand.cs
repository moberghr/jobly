using Jobly.Core.Handlers;

namespace Jobly.Tests.TestData.Handlers;

public class CounterCommand : IJobHandler<CounterRequest>
{
    public CounterCommand(CounterService counterService)
    {
        _counterService = counterService;
    }

    private readonly CounterService _counterService;

    public async Task HandleAsync(CounterRequest message, CancellationToken ct)
    {
        await Task.Delay(500, ct);

        _counterService.Increment();
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
