namespace Warp.Core.Logging;

/// <summary>
/// Constants for OpenTelemetry messaging-convention attribute keys plus warp.* extension keys.
/// Centralised here so call sites never typo a key like <c>messaging.operation.name</c>.
/// </summary>
public static class WarpTelemetryAttributes
{
    public const string MessagingSystem = "messaging.system";
    public const string MessagingOperationName = "messaging.operation.name";
    public const string MessagingOperationType = "messaging.operation.type";
    public const string MessagingDestinationName = "messaging.destination.name";
    public const string MessagingMessageId = "messaging.message.id";
    public const string MessagingConversationId = "messaging.message.conversation_id";
    public const string MessagingBatchMessageCount = "messaging.batch.message_count";
    public const string ErrorType = "error.type";

    public const string MessagingSystemValue = "warp";
    public const string OperationSend = "send";
    public const string OperationReceive = "receive";
    public const string OperationProcess = "process";

    public const string WarpJobKind = "warp.job.kind";
    public const string WarpJobType = "warp.job.type";
    public const string WarpJobScheduled = "warp.job.scheduled";
    public const string WarpJobAttempt = "warp.job.attempt";
    public const string WarpJobMaxAttempts = "warp.job.max_attempts";
    public const string WarpJobStatus = "warp.job.status";
    public const string WarpJobDurationMs = "warp.job.duration_ms";

    public const string WarpWorkerId = "warp.worker.id";
    public const string WarpWorkerGroup = "warp.worker.group";

    public const string WarpMediatorKind = "warp.mediator.kind";
    public const string WarpMediatorResponseType = "warp.mediator.response_type";
    public const string MediatorKindRequest = "request";
    public const string MediatorKindStream = "stream";

    public const string WarpTaskName = "warp.task.name";
    public const string WarpTaskLockHeld = "warp.task.lock_held";
    public const string WarpTaskMessage = "warp.task.message";

    public const string WarpMutexKey = "warp.mutex.key";
    public const string WarpMutexAcquired = "warp.mutex.acquired";
    public const string WarpMutexHeldByOtherEvent = "warp.mutex.held_by_other";

    /// <summary>
    /// Metadata dictionary keys read by the worker to enrich consumer-span tags. Mirrors property
    /// names on Warp.Core.Retry.IRetryMetadata so the worker can set retry tags without taking a
    /// project-level dependency on the addon. The unit test
    /// <c>WarpTelemetryTests.RetryMetadataKeys_MatchIRetryMetadataPropertyNames</c> pins these
    /// strings to the IRetryMetadata property names — a rename of either side breaks loudly.
    /// </summary>
    public const string RetryMetadataRetriedTimesKey = "RetriedTimes";

    public const string RetryMetadataMaxRetriesKey = "MaxRetries";
}
