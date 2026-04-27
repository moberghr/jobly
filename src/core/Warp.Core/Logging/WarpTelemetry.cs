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

    public static Activity StartJobActivity(Guid traceId, string? parentSpanId)
    {
        var activityTraceId = ActivityTraceId.CreateFromString(traceId.ToString("N").AsSpan());
        var activityParentSpanId = IsValidSpanId(parentSpanId)
            ? ActivitySpanId.CreateFromString(parentSpanId.AsSpan())
            : default;
        var parentContext = new ActivityContext(activityTraceId, activityParentSpanId, ActivityTraceFlags.None);

        return ActivitySource.StartActivity("Warp.Execute", ActivityKind.Consumer, parentContext)
            ?? new Activity("Warp.Execute")
                .SetIdFormat(ActivityIdFormat.W3C)
                .SetParentId(activityTraceId, activityParentSpanId, ActivityTraceFlags.None)
                .Start();
    }

    private static bool IsValidSpanId(string? value)
    {
        if (value == null || value.Length != 16)
        {
            return false;
        }

        return value.All(char.IsAsciiHexDigit);
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
}
