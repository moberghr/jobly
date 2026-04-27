using System.Diagnostics;
using Warp.Core.Handlers;

namespace Warp.Tests.TestData.Handlers;

public class ActivityCaptureRequest : IJob;

public class ActivityCaptureCommand : IJobHandler<ActivityCaptureRequest>
{
    private readonly ActivityCapture _capture;

    public ActivityCaptureCommand(ActivityCapture capture)
    {
        _capture = capture;
    }

    public Task HandleAsync(ActivityCaptureRequest message, CancellationToken cancellationToken)
    {
        var activity = Activity.Current;
        if (activity != null)
        {
            _capture.TraceId = activity.TraceId.ToHexString();
            _capture.SpanId = activity.SpanId.ToHexString();
            _capture.ParentSpanId = activity.ParentSpanId.ToHexString();
            _capture.Tags = activity.Tags.ToDictionary(x => x.Key, x => x.Value);
        }

        return Task.CompletedTask;
    }
}

public class ActivityCapture
{
    public string? TraceId { get; set; }

    public string? SpanId { get; set; }

    public string? ParentSpanId { get; set; }

    public Dictionary<string, string?> Tags { get; set; } = [];
}
