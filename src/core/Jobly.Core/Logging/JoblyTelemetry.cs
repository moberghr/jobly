using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Jobly.Core.Logging;

public static class JoblyTelemetry
{
    public const string ServiceName = "Jobly";

    public static readonly ActivitySource ActivitySource = new(ServiceName);
    public static readonly Meter Meter = new(ServiceName);

    public static readonly Histogram<double> JobDuration = Meter.CreateHistogram<double>(
        "jobly.job.duration",
        unit: "ms",
        description: "Duration of job handler execution");

    public static readonly UpDownCounter<long> JobsActive = Meter.CreateUpDownCounter<long>(
        "jobly.job.active",
        unit: "{job}",
        description: "Number of jobs currently being processed");

    public static readonly Counter<long> JobsCompleted = Meter.CreateCounter<long>(
        "jobly.job.completed",
        unit: "{job}",
        description: "Total jobs that finished processing");

    public static readonly Counter<long> JobsEnqueued = Meter.CreateCounter<long>(
        "jobly.job.enqueued",
        unit: "{job}",
        description: "Total jobs enqueued for processing");

    public static Activity StartJobActivity(Guid traceId, string? parentSpanId)
    {
        var activityTraceId = ActivityTraceId.CreateFromString(traceId.ToString("N").AsSpan());
        var activityParentSpanId = IsValidSpanId(parentSpanId)
            ? ActivitySpanId.CreateFromString(parentSpanId.AsSpan())
            : default;
        var parentContext = new ActivityContext(activityTraceId, activityParentSpanId, ActivityTraceFlags.None);

        return ActivitySource.StartActivity("Jobly.Execute", ActivityKind.Consumer, parentContext)
            ?? new Activity("Jobly.Execute")
                .SetIdFormat(ActivityIdFormat.W3C)
                .SetParentId(activityTraceId, activityParentSpanId, ActivityTraceFlags.None)
                .Start();
    }

    private static bool IsValidSpanId(string? value)
    {
        return value != null && value.Length == 16 && value.All(char.IsAsciiHexDigit);
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
