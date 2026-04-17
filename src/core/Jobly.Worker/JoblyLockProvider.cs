using Jobly.Core;
using Medallion.Threading;

namespace Jobly.Worker;

internal class JoblyLockProvider : IJoblyLockProvider
{
    private readonly IDistributedLockProvider _inner;

    public JoblyLockProvider(IDistributedLockProvider inner)
    {
        _inner = inner;
    }

    public async Task<IAsyncDisposable?> TryAcquireAsync(string name, TimeSpan timeout, CancellationToken ct)
    {
        var @lock = _inner.CreateLock(name);

        return await @lock.TryAcquireAsync(timeout, ct);
    }
}
