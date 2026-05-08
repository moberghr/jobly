using Warp.Core.Concurrency;
using Warp.Core.Handlers;

namespace Warp.Tests.TestData.Handlers;

public class SemaphoreAttributeCommand : IJobHandler<SemaphoreAttributeRequest>
{
    public Task HandleAsync(SemaphoreAttributeRequest message, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

[Semaphore("static-semaphore-key", 5)]
public class SemaphoreAttributeRequest : IJob;

public class SemaphoreSkipAttributeCommand : IJobHandler<SemaphoreSkipAttributeRequest>
{
    public Task HandleAsync(SemaphoreSkipAttributeRequest message, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

[Semaphore("static-semaphore-skip-key", 5, Mode = ConcurrencyMode.Skip)]
public class SemaphoreSkipAttributeRequest : IJob;

public class SemaphoreLimit5Command : IJobHandler<SemaphoreLimit5Request>
{
    private readonly ConcurrencyTracker _tracker;

    public SemaphoreLimit5Command(ConcurrencyTracker tracker)
    {
        _tracker = tracker;
    }

    public async Task HandleAsync(SemaphoreLimit5Request message, CancellationToken cancellationToken)
    {
        _tracker.Enter(message.Key);
        try
        {
            await Task.Delay(150, cancellationToken);
        }
        finally
        {
            _tracker.Exit(message.Key);
        }
    }
}

[Semaphore("limit-5-key", 5)]
public class SemaphoreLimit5Request : IJob
{
    public string Key { get; set; } = "limit-5-key";
}

public class MutexAndSemaphoreCommand : IJobHandler<MutexAndSemaphoreRequest>
{
    public Task HandleAsync(MutexAndSemaphoreRequest message, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

// When both attributes are present, [Mutex] wins (registration order in ConcurrencyPublishBehavior).
[Mutex("dual-attribute-key")]
[Semaphore("dual-attribute-key", 5)]
public class MutexAndSemaphoreRequest : IJob;

public class ThrowingConcurrencyCommand : IJobHandler<ThrowingConcurrencyRequest>
{
    public Task HandleAsync(ThrowingConcurrencyRequest message, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("Always fails — characterization test for slot release");
    }
}

public class ThrowingConcurrencyRequest : IJob;
