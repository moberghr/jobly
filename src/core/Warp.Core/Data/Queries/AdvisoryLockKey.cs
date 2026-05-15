using System.Text;

namespace Warp.Core.Data.Queries;

/// <summary>
/// Stable hash of a string lock key into a 64-bit signed integer suitable for PG's
/// <c>pg_try_advisory_xact_lock(bigint)</c>. The hash must be deterministic across processes
/// and runtime restarts — <c>String.GetHashCode</c> is randomized per-AppDomain and would
/// produce different keys on each server, defeating the lock. FNV-1a 64-bit is small, fast,
/// and deterministic, and is more than sufficient for the handful of well-known internal
/// lock keys Warp uses ("warp:scheduled-activation", "warp:message-routing", etc.) — the
/// collision space is large enough that real-world misses are vanishingly unlikely.
/// </summary>
public static class AdvisoryLockKey
{
    // FNV-1a 64-bit constants (RFC draft / official reference).
    private const ulong FnvOffsetBasis = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    public static long Compute(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        var bytes = Encoding.UTF8.GetBytes(key);
        var hash = FnvOffsetBasis;
        for (var i = 0; i < bytes.Length; i++)
        {
            hash ^= bytes[i];
            hash *= FnvPrime;
        }

        // Reinterpret the unsigned 64-bit hash as a signed 64-bit int for PG's bigint type.
        // Bit-for-bit identical, so equality is preserved across servers.
        return unchecked((long)hash);
    }
}
