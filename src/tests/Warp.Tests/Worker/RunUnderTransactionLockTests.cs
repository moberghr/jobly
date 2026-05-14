using Microsoft.EntityFrameworkCore;
using Shouldly;
using Warp.Core.Entities;
using Warp.Core.Enums;
using Warp.Tests.Fixtures;
using Warp.Tests.Helpers;

namespace Warp.Tests.Worker;

// Pins the contract of IWarpSqlQueries.RunUnderTransactionLockAsync — the xact-scoped
// advisory lock primitive that drives every server task with LocksWithTransaction = true.
// Correctness here directly determines whether server-task serialization actually works
// across multiple servers.
[GenerateDatabaseTests]
public abstract class RunUnderTransactionLockTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected RunUnderTransactionLockTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync() => await _fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [TimedFact]
    public async Task RunUnderTransactionLockAsync_WhenLockAvailable_RunsWorkAndCommits()
    {
        var queries = TestTasks.QueriesFor(_fixture.CreateContext());
        var jobId = Guid.NewGuid();

        await using var ctx = _fixture.CreateContext();
        var outcome = await queries.RunUnderTransactionLockAsync<int>(
            ctx,
            "warp:test:available",
            async (innerCtx, innerCt) =>
            {
                innerCtx.Set<Job>().Add(new Job
                {
                    Id = jobId,
                    Kind = JobKind.Job,
                    CurrentState = State.Enqueued,
                    Queue = "default",
                    Type = "TestType",
                    Message = "{}",
                    CreateTime = DateTime.UtcNow,
                    ScheduleTime = DateTime.UtcNow,
                });
                await innerCtx.SaveChangesAsync(innerCt);

                return 42;
            },
            default);

        outcome.LockHeld.ShouldBeTrue();
        outcome.Result.ShouldBe(42);

        // The row was actually committed — fresh context can read it.
        await using var readCtx = _fixture.CreateContext();
        (await readCtx.Set<Job>().AsNoTracking().AnyAsync(j => j.Id == jobId)).ShouldBeTrue();
    }

    [TimedFact]
    public async Task RunUnderTransactionLockAsync_WhenLockHeldByOther_ReturnsFalseWithoutRunning()
    {
        // Hold the lock from one context with a TaskCompletionSource gate so we can park
        // inside the work delegate while the second contender probes. This is the
        // textbook xact-lock contention scenario.
        var queries = TestTasks.QueriesFor(_fixture.CreateContext());
        var lockKey = "warp:test:contended-" + Guid.NewGuid().ToString("N");
        var insideHolder = new TaskCompletionSource();
        var releaseHolder = new TaskCompletionSource();
        var contenderRanWork = false;

        var holder = Task.Run(async () =>
        {
            await using var ctx = _fixture.CreateContext();
            return await queries.RunUnderTransactionLockAsync<int>(
                ctx,
                lockKey,
                async (_, _) =>
                {
                    insideHolder.SetResult();
                    await releaseHolder.Task;

                    return 1;
                },
                default);
        });

        await insideHolder.Task;

        // Holder is now sitting inside the lock with the tx open. A second caller on
        // the SAME key should observe (false, default) and skip its work entirely.
        await using var contenderCtx = _fixture.CreateContext();
        var contenderOutcome = await queries.RunUnderTransactionLockAsync<int>(
            contenderCtx,
            lockKey,
            (_, _) =>
            {
                contenderRanWork = true;

                return Task.FromResult(99);
            },
            default);

        contenderOutcome.LockHeld.ShouldBeFalse();
        contenderOutcome.Result.ShouldBe(default);
        contenderRanWork.ShouldBeFalse();

        // Let the holder finish.
        releaseHolder.SetResult();
        var holderOutcome = await holder;
        holderOutcome.LockHeld.ShouldBeTrue();
        holderOutcome.Result.ShouldBe(1);
    }

    [TimedFact]
    public async Task RunUnderTransactionLockAsync_WhenWorkThrows_RollsBackAndReleases()
    {
        var queries = TestTasks.QueriesFor(_fixture.CreateContext());
        var lockKey = "warp:test:throws-" + Guid.NewGuid().ToString("N");
        var jobId = Guid.NewGuid();

        await using var ctx = _fixture.CreateContext();
        var thrown = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await queries.RunUnderTransactionLockAsync<int>(
                ctx,
                lockKey,
                async (innerCtx, innerCt) =>
                {
                    innerCtx.Set<Job>().Add(new Job
                    {
                        Id = jobId,
                        Kind = JobKind.Job,
                        CurrentState = State.Enqueued,
                        Queue = "default",
                        Type = "TestType",
                        Message = "{}",
                        CreateTime = DateTime.UtcNow,
                        ScheduleTime = DateTime.UtcNow,
                    });
                    await innerCtx.SaveChangesAsync(innerCt);

                    throw new InvalidOperationException("intentional");
                },
                default));

        thrown.Message.ShouldBe("intentional");

        // Rolled back — the row should not be visible.
        await using var readCtx = _fixture.CreateContext();
        (await readCtx.Set<Job>().AsNoTracking().AnyAsync(j => j.Id == jobId)).ShouldBeFalse();

        // Lock is released (xact-scoped auto-release on rollback). A new caller on the
        // same key should be able to acquire and run.
        await using var afterCtx = _fixture.CreateContext();
        var after = await queries.RunUnderTransactionLockAsync<int>(
            afterCtx,
            lockKey,
            (_, _) => Task.FromResult(7),
            default);
        after.LockHeld.ShouldBeTrue();
        after.Result.ShouldBe(7);
    }

    [TimedFact]
    public async Task RunUnderTransactionLockAsync_DifferentKeys_DoNotBlockEachOther()
    {
        var queries = TestTasks.QueriesFor(_fixture.CreateContext());
        var keyA = "warp:test:keyA-" + Guid.NewGuid().ToString("N");
        var keyB = "warp:test:keyB-" + Guid.NewGuid().ToString("N");
        var insideA = new TaskCompletionSource();
        var releaseA = new TaskCompletionSource();

        var holderA = Task.Run(async () =>
        {
            await using var ctx = _fixture.CreateContext();
            return await queries.RunUnderTransactionLockAsync<int>(
                ctx,
                keyA,
                async (_, _) =>
                {
                    insideA.SetResult();
                    await releaseA.Task;

                    return 1;
                },
                default);
        });

        await insideA.Task;

        // Key B is independent — should acquire immediately, run, and return success.
        await using var ctxB = _fixture.CreateContext();
        var outcomeB = await queries.RunUnderTransactionLockAsync<int>(
            ctxB,
            keyB,
            (_, _) => Task.FromResult(2),
            default);

        outcomeB.LockHeld.ShouldBeTrue();
        outcomeB.Result.ShouldBe(2);

        releaseA.SetResult();
        await holderA;
    }
}
