using Warp.Core;
using Warp.Core.Handlers;
using Warp.Core.Helper;

namespace Warp.Tests.Http;

/// <summary>
/// Minimal in-memory <see cref="IPublisher"/> for tests that exercise the
/// <c>IRequest&lt;Guid&gt;</c> → <c>IPublisher.Enqueue</c> wrapper pattern without
/// requiring a real database. Records every Enqueue call for verification.
/// </summary>
public sealed class FakePublisher : IPublisher
{
    private readonly List<object> _enqueued = [];

    public IReadOnlyList<object> EnqueuedJobs => _enqueued;

    public Task<Guid> Enqueue<T>(T job)
        where T : class, IJob
    {
        _enqueued.Add(job);
        return Task.FromResult(Guid.NewGuid());
    }

    public Task<Guid> Enqueue<T>(T job, string? queue)
        where T : class, IJob
        => Enqueue(job);

    public Task<Guid> Enqueue<T>(T job, Guid parentJobId)
        where T : class, IJob
        => Enqueue(job);

    public Task<Guid> Enqueue<T>(T job, Guid parentJobId, string? queue)
        where T : class, IJob
        => Enqueue(job);

    public Task<Guid> Enqueue<T>(T job, JobParameters jobParameters)
        where T : class, IJob
        => Enqueue(job);

    public Task<Guid> Publish<T>(T message)
        where T : class, IMessage
    {
        _enqueued.Add(message);
        return Task.FromResult(Guid.NewGuid());
    }

    public Task<Guid> Publish<T>(T message, string? queue)
        where T : class, IMessage
        => Publish(message);

    public Task<Guid> Schedule<T>(T job, DateTime scheduleTime)
        where T : class, IJob
        => Enqueue(job);

    public Task<Guid> Schedule<T>(T job, DateTime scheduleTime, string? queue)
        where T : class, IJob
        => Enqueue(job);

    public Task<Guid> Schedule<T>(T job, DateTime scheduleTime, Guid parentJobId)
        where T : class, IJob
        => Enqueue(job);

    public Task<Guid> Schedule<T>(T job, DateTime scheduleTime, Guid parentJobId, string? queue)
        where T : class, IJob
        => Enqueue(job);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
