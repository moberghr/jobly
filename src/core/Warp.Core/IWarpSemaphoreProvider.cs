namespace Warp.Core;

public interface IWarpSemaphoreProvider
{
    Task<IAsyncDisposable?> TryAcquireAsync(string name, int maxCount, TimeSpan timeout, CancellationToken ct);
}
