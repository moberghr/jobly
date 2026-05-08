using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Warp.Core.Logging;

public static class WarpTelemetry
{
    public const string ServiceName = "Warp";

    public static readonly ActivitySource ActivitySource = new(ServiceName);
    public static readonly Meter Meter = new(ServiceName);

    public static readonly Histogram<double> JobDuration = Meter.CreateHistogram<double>(
        "warp.job.duration",
        unit: "ms",
        description: "Duration of job handler execution");

    public static readonly UpDownCounter<long> JobsActive = Meter.CreateUpDownCounter<long>(
        "warp.job.active",
        unit: "{job}",
        description: "Number of jobs currently being processed");

    public static readonly Counter<long> JobsCompleted = Meter.CreateCounter<long>(
        "warp.job.completed",
        unit: "{job}",
        description: "Total jobs that finished processing");

    public static readonly Counter<long> JobsEnqueued = Meter.CreateCounter<long>(
        "warp.job.enqueued",
        unit: "{job}",
        description: "Total jobs enqueued for processing");

    public static readonly Counter<long> NotificationsPublished = Meter.CreateCounter<long>(
        "warp.notifications.published",
        unit: "{notification}",
        description: "Total DB-push notifications successfully emitted by the transport");

    public static readonly Counter<long> NotificationPublishFailures = Meter.CreateCounter<long>(
        "warp.notifications.publish_failures",
        unit: "{notification}",
        description: "Total DB-push notifications that failed to publish (transport error). Each failure is also logged at Warning.");

    public static readonly Histogram<double> MediatorDuration = Meter.CreateHistogram<double>(
        "warp.mediator.duration",
        unit: "ms",
        description: "Duration of in-memory IRequest/IStreamRequest execution through IMediator");

    public static readonly UpDownCounter<long> MediatorInFlight = Meter.CreateUpDownCounter<long>(
        "warp.mediator.in_flight",
        unit: "{request}",
        description: "Number of in-memory mediator requests currently executing");

    /// <summary>
    /// Starts the consumer activity for handler execution when an <see cref="ActivityListener"/>
    /// is attached to the Warp source. Returns null when no listener is registered — workers
    /// must use the <c>?.</c> null-conditional operator on the result so non-OTel deployments
    /// pay zero allocation overhead. Span name follows the OpenTelemetry messaging-spans
    /// convention: <c>process &lt;queue&gt;</c>. Sets messaging.system / operation.name /
    /// operation.type / destination.name; the caller adds message-id, conversation-id, and
    /// warp.* tags.
    /// </summary>
    public static Activity? StartJobActivity(Guid traceId, string? parentSpanId, string queue)
    {
        var activityTraceId = ActivityTraceId.CreateFromString(traceId.ToString("N").AsSpan());
        var activityParentSpanId = IsValidSpanId(parentSpanId)
            ? ActivitySpanId.CreateFromString(parentSpanId.AsSpan())
            : default;
        var parentContext = new ActivityContext(activityTraceId, activityParentSpanId, ActivityTraceFlags.None);
        var spanName = $"{WarpTelemetryAttributes.OperationProcess} {queue}";

        var activity = ActivitySource.StartActivity(spanName, ActivityKind.Consumer, parentContext);
        if (activity == null)
        {
            return null;
        }

        activity.SetTag(WarpTelemetryAttributes.MessagingSystem, WarpTelemetryAttributes.MessagingSystemValue);
        activity.SetTag(WarpTelemetryAttributes.MessagingOperationName, WarpTelemetryAttributes.OperationProcess);
        activity.SetTag(WarpTelemetryAttributes.MessagingOperationType, WarpTelemetryAttributes.OperationProcess);
        activity.SetTag(WarpTelemetryAttributes.MessagingDestinationName, queue);

        return activity;
    }

    /// <summary>
    /// Back-compat shim. New code should call the three-arg overload with the actual queue name.
    /// </summary>
    public static Activity? StartJobActivity(Guid traceId, string? parentSpanId)
        => StartJobActivity(traceId, parentSpanId, "default");

    /// <summary>
    /// Starts a Producer-kind span for a publish operation. Span name "&lt;operation&gt; &lt;queue&gt;"
    /// per OTel messaging convention. Returns null when no listener is attached. The caller sets
    /// messaging.message.id and any per-publish tags after the row id is known.
    /// </summary>
    public static Activity? StartProducerActivity(string queue, string operation)
    {
        var activity = ActivitySource.StartActivity($"{operation} {queue}", ActivityKind.Producer);
        if (activity == null)
        {
            return null;
        }

        activity.SetTag(WarpTelemetryAttributes.MessagingSystem, WarpTelemetryAttributes.MessagingSystemValue);
        activity.SetTag(WarpTelemetryAttributes.MessagingOperationName, operation);
        activity.SetTag(WarpTelemetryAttributes.MessagingOperationType, operation);
        activity.SetTag(WarpTelemetryAttributes.MessagingDestinationName, queue);

        return activity;
    }

    /// <summary>
    /// Starts a Client-kind span for the worker's post-fetch / pre-handler bookkeeping.
    /// Span name "receive &lt;queue&gt;" per OTel messaging convention.
    /// </summary>
    public static Activity? StartReceiveActivity(string queue)
    {
        var activity = ActivitySource.StartActivity($"{WarpTelemetryAttributes.OperationReceive} {queue}", ActivityKind.Client);
        if (activity == null)
        {
            return null;
        }

        activity.SetTag(WarpTelemetryAttributes.MessagingSystem, WarpTelemetryAttributes.MessagingSystemValue);
        activity.SetTag(WarpTelemetryAttributes.MessagingOperationName, WarpTelemetryAttributes.OperationReceive);
        activity.SetTag(WarpTelemetryAttributes.MessagingOperationType, WarpTelemetryAttributes.OperationReceive);
        activity.SetTag(WarpTelemetryAttributes.MessagingDestinationName, queue);

        return activity;
    }

    /// <summary>
    /// Starts an Internal-kind span for in-memory mediator execution. Span name
    /// "process &lt;requestType&gt;"; the request type is treated as the destination so OTel
    /// consumers can filter on <c>messaging.destination.name</c> for in-process routing.
    /// </summary>
    public static Activity? StartMediatorActivity(string requestType, string responseType, string mediatorKind)
    {
        var activity = ActivitySource.StartActivity($"{WarpTelemetryAttributes.OperationProcess} {requestType}", ActivityKind.Internal);
        if (activity == null)
        {
            return null;
        }

        activity.SetTag(WarpTelemetryAttributes.MessagingSystem, WarpTelemetryAttributes.MessagingSystemValue);
        activity.SetTag(WarpTelemetryAttributes.MessagingOperationName, WarpTelemetryAttributes.OperationProcess);
        activity.SetTag(WarpTelemetryAttributes.MessagingOperationType, WarpTelemetryAttributes.OperationProcess);
        activity.SetTag(WarpTelemetryAttributes.MessagingDestinationName, requestType);
        activity.SetTag(WarpTelemetryAttributes.WarpMediatorKind, mediatorKind);
        activity.SetTag(WarpTelemetryAttributes.WarpMediatorResponseType, responseType);

        return activity;
    }

    /// <summary>
    /// Starts an Internal-kind span around a single server-task iteration.
    /// Span name "warp.server_task &lt;taskName&gt;".
    /// </summary>
    public static Activity? StartServerTaskActivity(string taskName)
    {
        var activity = ActivitySource.StartActivity($"warp.server_task {taskName}", ActivityKind.Internal);
        activity?.SetTag(WarpTelemetryAttributes.WarpTaskName, taskName);

        return activity;
    }

    /// <summary>
    /// Starts an Internal-kind span around a concurrency-control acquire attempt (Mutex or
    /// Semaphore). Span name "warp.concurrency_acquire". Caller stamps warp.concurrency.key,
    /// warp.concurrency.limit, and warp.concurrency.acquired before disposing.
    /// </summary>
    public static Activity? StartConcurrencyActivity() => ActivitySource.StartActivity("warp.concurrency_acquire", ActivityKind.Internal);

    /// <summary>
    /// Bound the length of a string used as an OTel span status description. Activity status
    /// descriptions go to OTel exporters and tracing backends; arbitrarily-long exception
    /// messages would bloat span payloads and make UIs unreadable. 256 chars is the convention
    /// used by all worker / mediator / server-task error paths.
    /// </summary>
    internal static string TruncateMessage(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    public static string GetShortTypeName(string? assemblyQualifiedName)
    {
        if (assemblyQualifiedName == null)
        {
            return "unknown";
        }

        var commaIndex = assemblyQualifiedName.IndexOf(',', StringComparison.Ordinal);

        return commaIndex > 0 ? assemblyQualifiedName[..commaIndex] : assemblyQualifiedName;
    }

    private static bool IsValidSpanId(string? value)
    {
        if (value == null || value.Length != 16)
        {
            return false;
        }

        return value.All(char.IsAsciiHexDigit);
    }
}
