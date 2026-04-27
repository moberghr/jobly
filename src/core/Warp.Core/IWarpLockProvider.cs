namespace Warp.Core;

public interface IWarpLockProvider
{
    Task<IAsyncDisposable?> TryAcquireAsync(string name, TimeSpan timeout, CancellationToken ct);
}
