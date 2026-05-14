using Microsoft.EntityFrameworkCore;
using Shouldly;
using Warp.Core.Data.Entities;
using Warp.Core.Entities;
using Warp.Core.Enums;
using Warp.Tests.Fixtures;
using Warp.Tests.Helpers;

namespace Warp.Tests.Reliability;

// Pins the outer-transaction detection in StaleJobRecovery and ServerCleanup. Both tasks
// use FOR NO KEY UPDATE row locks, which require a wrapping transaction. ServerTaskLoop's
// xact-lock path provides the wrap on the production hot path; direct callers (admin,
// tests) don't get it. The tasks must:
//   1) open their own tx when there's no outer one (otherwise FOR NO KEY UPDATE drops its
//      lock the moment the SELECT returns), and
//   2) NOT open a nested tx when there IS an outer one (SQL Server throws on nested
//      BeginTransaction; PG silently degrades to savepoints — neither is what we want).
// If anyone regresses this to unconditionally call BeginTransactionAsync, the
// _WrappedInOuterTransaction_ tests below will throw InvalidOperationException on MSSQL.
[GenerateDatabaseTests]
public abstract class OuterTransactionDetectionTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected OuterTransactionDetectionTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync() => await _fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [TimedFact]
    public async Task StaleJobRecovery_NoOuterTransaction_OpensOwnAndCommits()
    {
        var jobId = await SeedStaleProcessingJobAsync();

        var ctx = _fixture.CreateContext();
        ctx.Database.CurrentTransaction.ShouldBeNull("precondition: no outer transaction");

        await TestTasks
            .CreateStaleJobRecovery(ctx, TimeProvider.System, TimeSpan.FromMinutes(5))
            .RecoverStaleJobsAsync(CancellationToken.None);

        // Committed — a fresh context can see the requeued state. If the method had failed
        // to open its own tx, FOR NO KEY UPDATE would have lost the row lock immediately
        // after SELECT and the requeue could either fail or race; either way the assertion
        // is on the committed end-state.
        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().AsNoTracking().FirstAsync(j => j.Id == jobId);
        job.CurrentState.ShouldBe(State.Enqueued);
    }

    [TimedFact]
    public async Task StaleJobRecovery_WithOuterTransaction_ReusesItWithoutNestingException()
    {
        var jobId = await SeedStaleProcessingJobAsync();

        var ctx = _fixture.CreateContext();
        await using var outerTx = await ctx.Database.BeginTransactionAsync();
        ctx.Database.CurrentTransaction.ShouldNotBeNull("precondition: outer transaction is open");

        // If the detection regressed and the method unconditionally called
        // BeginTransactionAsync, this would throw InvalidOperationException on MSSQL
        // ("The connection is already in a transaction and cannot participate in another
        // transaction"). On PG it would silently create a savepoint, which is also wrong
        // because the outer commit/rollback semantics would diverge from the inner ones.
        var result = await TestTasks
            .CreateStaleJobRecovery(ctx, TimeProvider.System, TimeSpan.FromMinutes(5))
            .RecoverStaleJobsAsync(CancellationToken.None);

        result.Requeued.ShouldBe(1);

        await outerTx.CommitAsync();

        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().AsNoTracking().FirstAsync(j => j.Id == jobId);
        job.CurrentState.ShouldBe(State.Enqueued);
    }

    [TimedFact]
    public async Task StaleJobRecovery_WithOuterTransactionRolledBack_ChangesDoNotPersist()
    {
        var jobId = await SeedStaleProcessingJobAsync();

        var ctx = _fixture.CreateContext();
        await using var outerTx = await ctx.Database.BeginTransactionAsync();

        await TestTasks
            .CreateStaleJobRecovery(ctx, TimeProvider.System, TimeSpan.FromMinutes(5))
            .RecoverStaleJobsAsync(CancellationToken.None);

        // Rolling back the OUTER tx must roll back the inner work too — proves the inner
        // method did not silently commit its own nested tx.
        await outerTx.RollbackAsync();

        var readCtx = _fixture.CreateContext();
        var job = await readCtx.Set<Job>().AsNoTracking().FirstAsync(j => j.Id == jobId);
        job.CurrentState.ShouldBe(State.Processing, "outer rollback must undo inner mutations");
    }

    [TimedFact]
    public async Task ServerCleanup_NoOuterTransaction_OpensOwnAndCommits()
    {
        var serverId = await SeedStaleServerAsync();

        var ctx = _fixture.CreateContext();
        ctx.Database.CurrentTransaction.ShouldBeNull("precondition: no outer transaction");

        var removed = await TestTasks
            .CreateServerCleanup(ctx, TimeProvider.System, TimeSpan.FromMinutes(5))
            .CleanUpServersAsync(CancellationToken.None);

        removed.ShouldBe(1);

        var readCtx = _fixture.CreateContext();
        (await readCtx.Set<Server>().AsNoTracking().AnyAsync(s => s.Id == serverId)).ShouldBeFalse();
    }

    [TimedFact]
    public async Task ServerCleanup_WithOuterTransaction_ReusesItWithoutNestingException()
    {
        var serverId = await SeedStaleServerAsync();

        var ctx = _fixture.CreateContext();
        await using var outerTx = await ctx.Database.BeginTransactionAsync();
        ctx.Database.CurrentTransaction.ShouldNotBeNull("precondition: outer transaction is open");

        var removed = await TestTasks
            .CreateServerCleanup(ctx, TimeProvider.System, TimeSpan.FromMinutes(5))
            .CleanUpServersAsync(CancellationToken.None);

        removed.ShouldBe(1);

        await outerTx.CommitAsync();

        var readCtx = _fixture.CreateContext();
        (await readCtx.Set<Server>().AsNoTracking().AnyAsync(s => s.Id == serverId)).ShouldBeFalse();
    }

    [TimedFact]
    public async Task ServerCleanup_WithOuterTransactionRolledBack_ServerStillPresent()
    {
        var serverId = await SeedStaleServerAsync();

        var ctx = _fixture.CreateContext();
        await using var outerTx = await ctx.Database.BeginTransactionAsync();

        await TestTasks
            .CreateServerCleanup(ctx, TimeProvider.System, TimeSpan.FromMinutes(5))
            .CleanUpServersAsync(CancellationToken.None);

        await outerTx.RollbackAsync();

        var readCtx = _fixture.CreateContext();
        (await readCtx.Set<Server>().AsNoTracking().AnyAsync(s => s.Id == serverId))
            .ShouldBeTrue("outer rollback must undo the server removal");
    }

    private async Task<Guid> SeedStaleProcessingJobAsync()
    {
        await using var ctx = _fixture.CreateContext();
        var jobId = Guid.NewGuid();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Processing,
            CreateTime = DateTime.UtcNow,
            ScheduleTime = DateTime.UtcNow,
            Queue = "default",
            LastKeepAlive = DateTime.UtcNow.AddMinutes(-10),
        });
        await ctx.SaveChangesAsync();

        return jobId;
    }

    private async Task<Guid> SeedStaleServerAsync()
    {
        await using var ctx = _fixture.CreateContext();
        var serverId = Guid.NewGuid();
        ctx.Set<Server>().Add(new Server
        {
            Id = serverId,
            ServerName = "stale-server-" + Guid.NewGuid().ToString("N"),
            StartedTime = DateTime.UtcNow.AddHours(-1),
            LastHeartbeatTime = DateTime.UtcNow.AddMinutes(-10),
        });
        await ctx.SaveChangesAsync();

        return serverId;
    }
}
