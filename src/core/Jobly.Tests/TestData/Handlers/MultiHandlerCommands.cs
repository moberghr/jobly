using Jobly.Core.Handlers;

namespace Jobly.Tests.TestData.Handlers;

public class MultiRequest : IJob { }

public class MultiHandlerCounter
{
    public int CountA;
    public int CountB;

    public void IncrementA() => Interlocked.Increment(ref CountA);
    public void IncrementB() => Interlocked.Increment(ref CountB);
}

public class MultiHandlerA : IJobHandler<MultiRequest>
{
    private readonly MultiHandlerCounter _counter;

    public MultiHandlerA(MultiHandlerCounter counter)
    {
        _counter = counter;
    }

    public Task HandleAsync(MultiRequest message, CancellationToken cancellationToken)
    {
        _counter.IncrementA();
        return Task.CompletedTask;
    }
}

public class MultiHandlerB : IJobHandler<MultiRequest>
{
    private readonly MultiHandlerCounter _counter;

    public MultiHandlerB(MultiHandlerCounter counter)
    {
        _counter = counter;
    }

    public Task HandleAsync(MultiRequest message, CancellationToken cancellationToken)
    {
        _counter.IncrementB();
        return Task.CompletedTask;
    }
}
