namespace Warp.Core.Data.Entities;

/// <summary>
/// Index row that connects a saga instance to a routed handler-job. Written by
/// <c>SagaHandlerProxy</c> on each invocation so the dashboard's activity log can find every
/// job that touched a saga by primary-key range-scan instead of by string-searching
/// <c>Job.HandlerType</c> and JSON-parsing <c>Job.Message</c>.
/// </summary>
/// <remarks>
/// Deliberately a separate table from <c>Job</c>: saga concerns stay in saga-owned storage,
/// the Job table remains single-purpose for the execution-orchestration state machine. Per
/// CLAUDE.md §2.1.
///
/// Rows are deleted when the saga completes (<c>MarkCompleted</c>) — the activity log is only
/// meaningful for live sagas. No retention/TTL needed.
/// </remarks>
public class SagaJobLink
{
    public Guid SagaId { get; set; }

    public Guid JobId { get; set; }

    public DateTime CreatedAt { get; set; }
}
