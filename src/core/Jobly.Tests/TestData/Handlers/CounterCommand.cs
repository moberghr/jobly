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
        await Task.Delay(500);

        _counterService.Increment();
    }
}

public class CounterRequest : IJob
{

}

public class CounterService
{
    public int Counter = 0;

    public void Increment()
    {
        Interlocked.Increment(ref Counter);
    }
}