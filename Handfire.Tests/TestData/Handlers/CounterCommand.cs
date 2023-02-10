using MediatR;

namespace Handfire.Tests.TestData.Handlers;
public class CounterCommand : IRequestHandler<CounterRequest, Unit>
{
    public CounterCommand(CounterService counterService)
    {
        _counterService = counterService;
    }

    private readonly CounterService _counterService;

    public async Task<Unit> Handle(CounterRequest request, CancellationToken cancellationToken)
    {
        await Task.Delay(500);

        _counterService.Increment();

        return Unit.Value;
    }
}

public class CounterRequest : IRequest<Unit>
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