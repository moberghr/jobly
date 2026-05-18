using Microsoft.EntityFrameworkCore;
using Warp.Core.Data.Entities;

namespace Warp.Tests.Fixtures;

/// <summary>
/// Shared test-infrastructure helpers for the background-services feature. The
/// <c>BackgroundServiceInstance / Lease / Log</c> entities have non-nullable FK references
/// to <c>Server.Id</c>; every test seeding those rows directly must first seed a Server
/// row with the same Id, or PG/SQL Server raise FK violations on INSERT.
/// </summary>
public static class BackgroundServiceTestSeed
{
    /// <summary>
    /// Inserts a <see cref="Server"/> row with the supplied <paramref name="id"/> and an
    /// optional human-readable <paramref name="name"/>. Idempotent in the sense that
    /// repeated calls with the same id throw a PK violation — callers are expected to
    /// seed each unique server id exactly once per test scope.
    /// </summary>
    public static async Task SeedServerAsync(
        this IDatabaseFixture fixture,
        Guid id,
        string? name = null,
        CancellationToken ct = default)
    {
        var ctx = fixture.CreateContext();
        var now = DateTime.UtcNow;
        ctx.Set<Server>().Add(new Server
        {
            Id = id,
            ServerName = name ?? $"test-server-{id:N}"[..32],
            StartedTime = now,
            LastHeartbeatTime = now,
        });

        await ctx.SaveChangesAsync(ct);
    }
}
