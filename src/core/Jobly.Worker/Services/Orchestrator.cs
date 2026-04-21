using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Jobly.Worker.Services;

/// <summary>
/// Runs one orchestration pass per iteration: finalize parents whose children have all
/// reached terminal state, activate continuations whose parent is now terminal, and fail
/// children of deleted parents. Wake-up on <c>JobFinalized</c> events (worker completions
/// and push notifications) is routed through
/// <see cref="ServerTaskSignals{TContext}.SignalJobFinalized"/>.
/// </summary>
public sealed class Orchestrator<TContext> : IServerTask
    where TContext : DbContext
{
    private readonly TContext _context;
    private readonly TimeProvider _time;
    private readonly JoblyWorkerConfiguration _configuration;

    public Orchestrator(
        TContext context,
        TimeProvider time,
        IOptions<JoblyWorkerConfiguration> configuration)
    {
        _context = context;
        _time = time;
        _configuration = configuration.Value;
    }

    public string Name => "Orchestration";

    public string? LockKey => "jobly:orchestration";

    public TimeSpan? DefaultInterval => _configuration.OrchestrationInterval;

    public async Task<string?> ExecuteAsync(CancellationToken ct)
    {
        var workDone = await RunOrchestrationCoreAsync(ct);

        return workDone ? "Orchestration pass completed" : null;
    }

    internal async Task<bool> RunOrchestrationCoreAsync(CancellationToken ct)
    {
        var jobExpirationTimeout = _configuration.JobExpirationTimeout;

        var finalized = await FinalizeParentsAsync(jobExpirationTimeout, ct);
        _context.ChangeTracker.Clear();
        var activated = await ActivateContinuationsAsync(ct);
        _context.ChangeTracker.Clear();
        var cleaned = await FailChildrenOfDeletedParentsAsync(jobExpirationTimeout, ct);
        _context.ChangeTracker.Clear();

        return finalized > 0 || activated > 0 || cleaned > 0;
    }

    private async Task<int> FinalizeParentsAsync(TimeSpan jobExpirationTimeout, CancellationToken ct)
    {
        var readyParents = await _context.Set<Job>()
            .Where(p => (p.Kind == JobKind.Message || p.Kind == JobKind.Batch)
                && (p.CurrentState == State.Awaiting || p.CurrentState == State.Processing))
            .Where(p => !_context.Set<Job>()
                .Any(c => c.ParentJobId == p.Id && c.Kind == JobKind.Job
                    && c.CurrentState != State.Completed && c.CurrentState != State.Failed
                    && c.CurrentState != State.Awaiting))
            .Where(p => _context.Set<Job>()
                .Any(c => c.ParentJobId == p.Id && c.Kind == JobKind.Job
                    && (c.CurrentState == State.Completed || c.CurrentState == State.Failed)))
            .ToListAsync(ct);

        if (readyParents.Count == 0)
        {
            return 0;
        }

        var now = _time.GetUtcNow().UtcDateTime;
        foreach (var parent in readyParents)
        {
            var continuationOptions = parent.ContinuationOptions ?? ContinuationOptions.OnlyOnSucceeded;

            var hasFailedChildren = await _context.Set<Job>()
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

        await _context.SaveChangesAsync(ct);

        return readyParents.Count;
    }

    private async Task<int> ActivateContinuationsAsync(CancellationToken ct)
    {
        var awaitingChildren = await _context.Set<Job>()
            .AsNoTracking()
            .Where(c => c.CurrentState == State.Awaiting && c.ParentJobId != null)
            .Where(c => _context.Set<Job>().Any(p =>
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
                await _context.Set<Job>()
                    .Where(x => x.Id == childId && x.CurrentState == State.Awaiting)
                    .ExecuteUpdateAsync(x => x.SetProperty(p => p.CurrentState, State.Processing), ct);

                activated += await _context.Set<Job>()
                    .Where(x => x.ParentJobId == childId && x.CurrentState == State.Awaiting && x.Kind == JobKind.Job)
                    .ExecuteUpdateAsync(x => x.SetProperty(p => p.CurrentState, State.Enqueued), ct);
            }
            else
            {
                activated += await _context.Set<Job>()
                    .Where(x => x.Id == childId && x.CurrentState == State.Awaiting)
                    .ExecuteUpdateAsync(x => x.SetProperty(p => p.CurrentState, State.Enqueued), ct);
            }
        }

        return activated;
    }

    private async Task<int> FailChildrenOfDeletedParentsAsync(TimeSpan jobExpirationTimeout, CancellationToken ct)
    {
        var now = _time.GetUtcNow().UtcDateTime;

        var orphaned = await _context.Set<Job>()
            .Where(c => c.CurrentState == State.Awaiting && c.ParentJobId != null)
            .Where(c => _context.Set<Job>().Any(p =>
                p.Id == c.ParentJobId && p.CurrentState == State.Deleted))
            .ToListAsync(ct);

        if (orphaned.Count == 0)
        {
            return 0;
        }

        foreach (var child in orphaned)
        {
            child.CurrentState = State.Failed;
            child.ExpireAt = now.Add(jobExpirationTimeout);

            _context.Set<JobLog>().Add(new JobLog
            {
                JobId = child.Id,
                EventType = "Failed",
                Timestamp = now,
                Level = "Warning",
                Message = "Failed — parent job was deleted",
            });

            if (child.Kind == JobKind.Batch)
            {
                var batchChildIds = await _context.Set<Job>()
                    .Where(x => x.ParentJobId == child.Id && x.CurrentState == State.Awaiting)
                    .Select(x => x.Id)
                    .ToListAsync(ct);

                await _context.Set<Job>()
                    .Where(x => x.ParentJobId == child.Id && x.CurrentState == State.Awaiting)
                    .ExecuteUpdateAsync(
                        x => x
                            .SetProperty(p => p.CurrentState, State.Failed)
                            .SetProperty(p => p.ExpireAt, now.Add(jobExpirationTimeout)),
                        ct);

                foreach (var batchChildId in batchChildIds)
                {
                    _context.Set<JobLog>().Add(new JobLog
                    {
                        JobId = batchChildId,
                        EventType = "Failed",
                        Timestamp = now,
                        Level = "Warning",
                        Message = "Failed — parent batch was deleted",
                    });
                }
            }
        }

        await _context.SaveChangesAsync(ct);

        return orphaned.Count;
    }
}
