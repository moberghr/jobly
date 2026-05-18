using Warp.Core.Data.Entities;

namespace Warp.Core.BackgroundServices;

/// <summary>
/// Persists a batch of <see cref="BackgroundServiceLog"/> entries in a single round-trip.
/// Resolved via DI scope per flush by <c>BackgroundServiceLogCollector</c>.
/// </summary>
public interface IBackgroundServiceLogStore
{
    /// <summary>
    /// Inserts all <paramref name="entries"/> in a single batched write.
    /// </summary>
    Task InsertManyAsync(IReadOnlyList<BackgroundServiceLog> entries, CancellationToken ct);
}
