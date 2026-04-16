namespace Jobly.Core;

public interface IJoblyLockProvider
{
    Task<IAsyncDisposable?> TryAcquireAsync(string name, TimeSpan timeout, CancellationToken ct);
}
