using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobly.Worker.Services;

public class OrchestrationTask<TContext> : ServerTaskBase<TContext>
    where TContext : DbContext
{
    private static OrchestrationTask<TContext>? _instance;

    public OrchestrationTask(
        IServiceScopeFactory scopeFactory,
        ILogger<OrchestrationTask<TContext>> logger,
        IOptions<JoblyWorkerConfiguration> configuration,
        IDistributedLockProvider lockProvider,
        TimeProvider timeProvider)
        : base(scopeFactory, logger, configuration, timeProvider, "jobly:orchestration", lockProvider)
    {
        _instance = this;
    }

    protected override string TaskName => "Orchestration";

    protected override TimeSpan DefaultInterval => Configuration.OrchestrationInterval;

    /// <summary>
    /// Signal the orchestrator to wake up and check for work.
    /// Called by workers after job completion.
    /// </summary>
    public static void SignalOrchestrator() => _instance?.Signal();

    protected override async Task<string?> RunServerTask(TContext context, CancellationToken ct)
    {
        var workDone = await RunOrchestration(context, TimeProvider, Configuration.JobExpirationTimeout, ct);
        return workDone ? "Orchestration pass completed" : null;
    }

    /// <summary>
    /// Runs one orchestration pass: finalize parents + activate continuations.
    /// Returns true if any work was done (caller should loop).
    /// Public static so tests can call it directly.
    /// </summary>
    public static async Task<bool> RunOrchestration<TCtx>(TCtx context, TimeProvider timeProvider, TimeSpan jobExpirationTimeout, CancellationToken ct)
        where TCtx : DbContext
    {
        // Clear change tracker between steps to avoid stale tracked entities
        var finalized = await FinalizeParents(context, timeProvider, jobExpirationTimeout, ct);
        context.ChangeTracker.Clear();
        var activated = await ActivateContinuations(context, ct);
        context.ChangeTracker.Clear();
        var cleaned = await CleanupOrphanedChildren(context, timeProvider, jobExpirationTimeout, ct);
        context.ChangeTracker.Clear();
        return finalized > 0 || activated > 0 || cleaned > 0;
    }

    /// <summary>
    /// Find parents (Message/Batch) that are still active but have ALL non-awaiting children in terminal state.
    /// Awaiting children are continuations — they don't block parent finalization.
    /// A parent must have at least one child in terminal state to be finalized (prevents premature finalization
    /// of continuation batches whose children are all still Awaiting).
    /// </summary>
    private static async Task<int> FinalizeParents<TCtx>(TCtx context, TimeProvider timeProvider, TimeSpan jobExpirationTimeout, CancellationToken ct)
        where TCtx : DbContext
    {
        // Find parents where:
        // 1. No children are in active (non-terminal, non-awaiting) state (Enqueued/Processing)
        // 2. At least one child is in terminal state (Completed/Failed) — prevents premature finalization
        var readyParents = await context.Set<Job>()
            .Where(p => (p.Kind == JobKind.Message || p.Kind == JobKind.Batch)
                && (p.CurrentState == State.Awaiting || p.CurrentState == State.Processing))
            .Where(p => !context.Set<Job>()
                .Any(c => c.ParentJobId == p.Id && c.Kind == JobKind.Job
                    && c.CurrentState != State.Completed && c.CurrentState != State.Failed
                    && c.CurrentState != State.Awaiting))
            .Where(p => context.Set<Job>()
                .Any(c => c.ParentJobId == p.Id && c.Kind == JobKind.Job
                    && (c.CurrentState == State.Completed || c.CurrentState == State.Failed)))
            .ToListAsync(ct);

        if (readyParents.Count == 0)
        {
            return 0;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var parent in readyParents)
        {
            var continuationOptions = parent.ContinuationOptions ?? ContinuationOptions.OnlyOnSucceeded;

            var hasFailedChildren = await context.Set<Job>()
                .Where(c => c.ParentJobId == parent.Id && c.Kind == JobKind.Job && c.CurrentState == State.Failed)
                .AnyAsync(ct);

            if (hasFailedChildren && continuationOptions != ContinuationOptions.OnAnyFinishedState)
            {
                parent.CurrentState = State.Failed;
            }
            else
            {
                parent.CurrentState = State.Completed;
            }

            parent.ExpireAt = now.Add(jobExpirationTimeout);
        }

        await context.SaveChangesAsync(ct);
        return readyParents.Count;
    }

    /// <summary>
    /// Find Awaiting children whose parent is in a terminal state and activate them.
    /// Handles both regular continuations and batch continuations.
    /// </summary>
    private static async Task<int> ActivateContinuations<TCtx>(TCtx context, CancellationToken ct)
        where TCtx : DbContext
    {
        // Find awaiting children whose parent allows activation, load them fresh
        var awaitingChildren = await context.Set<Job>()
            .AsNoTracking()
            .Where(c => c.CurrentState == State.Awaiting && c.ParentJobId != null)
            .Where(c => context.Set<Job>().Any(p =>
                p.Id == c.ParentJobId
                && (p.CurrentState == State.Completed
                    || (p.CurrentState == State.Failed && p.ContinuationOptions == ContinuationOptions.OnAnyFinishedState))))
            .ToListAsync(ct);

        if (awaitingChildren.Count == 0)
        {
            return 0;
        }

        var activated = 0;
        foreach (var child in awaitingChildren)
        {
            var childId = child.Id;
            if (child.Kind == JobKind.Batch)
            {
                // Activate the batch's own children (set Awaiting → Enqueued)
                activated += await context.Set<Job>()
                    .Where(x => x.ParentJobId == childId && x.CurrentState == State.Awaiting && x.Kind == JobKind.Job)
                    .ExecuteUpdateAsync(x => x.SetProperty(p => p.CurrentState, State.Enqueued), ct);
            }
            else
            {
                // Activate regular continuation job
                activated += await context.Set<Job>()
                    .Where(x => x.Id == childId && x.CurrentState == State.Awaiting)
                    .ExecuteUpdateAsync(x => x.SetProperty(p => p.CurrentState, State.Enqueued), ct);
            }
        }

        return activated;
    }

    /// <summary>
    /// Clean up Awaiting children whose parent is in a state that will never activate them:
    /// - Parent is Deleted
    /// - Parent is Failed with ContinuationOptions = OnlyOnSucceeded (default)
    /// These children would otherwise be stuck in Awaiting state forever.
    /// </summary>
    private static async Task<int> CleanupOrphanedChildren<TCtx>(TCtx context, TimeProvider timeProvider, TimeSpan jobExpirationTimeout, CancellationToken ct)
        where TCtx : DbContext
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // Find Awaiting children whose parent will never activate them
        var orphaned = await context.Set<Job>()
            .Where(c => c.CurrentState == State.Awaiting && c.ParentJobId != null)
            .Where(c => context.Set<Job>().Any(p =>
                p.Id == c.ParentJobId
                && (p.CurrentState == State.Deleted
                    || (p.CurrentState == State.Failed
                        && (p.ContinuationOptions == null || p.ContinuationOptions == ContinuationOptions.OnlyOnSucceeded)))))
            .ToListAsync(ct);

        if (orphaned.Count == 0)
        {
            return 0;
        }

        foreach (var child in orphaned)
        {
            child.CurrentState = State.Deleted;
            child.ExpireAt = now.Add(jobExpirationTimeout);

            // If this is a batch, also delete its children
            if (child.Kind == JobKind.Batch)
            {
                await context.Set<Job>()
                    .Where(x => x.ParentJobId == child.Id && x.CurrentState == State.Awaiting)
                    .ExecuteUpdateAsync(x => x
                        .SetProperty(p => p.CurrentState, State.Deleted)
                        .SetProperty(p => p.ExpireAt, now.Add(jobExpirationTimeout)), ct);
            }
        }

        await context.SaveChangesAsync(ct);
        return orphaned.Count;
    }
}
