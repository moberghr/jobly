using System.Collections.Concurrent;
using System.Diagnostics;
using Warp.Core.Logging;

namespace Warp.Tests.Helpers;

/// <summary>
/// Test helper that registers an <see cref="ActivityListener"/> for the Warp <see cref="ActivitySource"/>
/// and captures Activities started inside the test's async-flow scope.
///
/// **Isolation model.** The harness is process-global by .NET design — `ActivitySource.AddActivityListener`
/// receives every span emitted in the AppDomain. Without scoping, two concurrent test classes' harnesses
/// would each capture the other's spans, producing flaky tests for any assertion that doesn't filter by
/// a specific id. To avoid that, each harness installs a unique <c>_sentinel</c> object into a static
/// <see cref="AsyncLocal{T}"/> on construction. The listener's <c>Sample</c> + <c>ActivityStarted</c>
/// callbacks check the AsyncLocal: an Activity is "ours" only if it was created on a thread whose
/// async-flow inherits from the same harness's construction frame. Background workers spawned during
/// the test (e.g. <c>WarpTestServer</c>'s `IHost.StartAsync`) inherit ExecutionContext including the
/// AsyncLocal, so their spans count as ours. A peer test class's harness runs in a sibling async flow
/// with a different sentinel, so its Activities aren't captured here.
///
/// Net effect: tests using this harness don't need to filter captures by message-id; assertions like
/// <c>FirstByName(...)</c> and <c>Captured.ShouldBeEmpty()</c> are reliable across parallel runs.
///
/// **WHERE TO CONSTRUCT.** Create the harness in either:
/// (a) the test class constructor (synchronous; AsyncLocal write captured into the test method's
///     ExecutionContext), or
/// (b) the test method body, before the first <c>await</c>.
///
/// **DO NOT** create it inside <c>IAsyncLifetime.InitializeAsync</c> or any other <c>async</c> method
/// run by xUnit's lifecycle. AsyncLocal mutations made inside an async method do not propagate back
/// to the caller in .NET — by the time xUnit invokes the test method, the sentinel set in
/// <c>InitializeAsync</c> is gone and the listener samples every Activity as <c>None</c>. Symptoms:
/// <c>StartJobActivity</c> returns null, the worker emits no span, <c>Activity.Current</c> is null
/// inside the handler, and assertions on captured tags fail with "key not present in dictionary".
/// </summary>
internal sealed class ActivityListenerHarness : IDisposable
{
    private static readonly AsyncLocal<object?> _activeSentinel = new();

    private readonly object _sentinel = new();
    private readonly object? _previousSentinel;
    private readonly ActivityListener _listener;
    private readonly List<Activity> _captured = [];
    private readonly ConcurrentDictionary<string, byte> _ours = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    public ActivityListenerHarness()
    {
        _previousSentinel = _activeSentinel.Value;
        _activeSentinel.Value = _sentinel;

        _listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, WarpTelemetry.ServiceName, StringComparison.Ordinal),
            Sample = SampleByAsyncLocal,
            SampleUsingParentId = SampleByAsyncLocalParentId,
            ActivityStarted = OnActivityStarted,
            ActivityStopped = OnActivityStopped,
        };

        ActivitySource.AddActivityListener(_listener);
    }

    public IReadOnlyList<Activity> Captured
    {
        get
        {
            lock (_lock)
            {
                return [.. _captured];
            }
        }
    }

    public Activity? FirstByName(string operationName)
    {
        lock (_lock)
        {
            return _captured.FirstOrDefault(x => string.Equals(x.OperationName, operationName, StringComparison.Ordinal));
        }
    }

    public IReadOnlyList<Activity> AllByName(string operationName)
    {
        lock (_lock)
        {
            return [.. _captured.Where(x => string.Equals(x.OperationName, operationName, StringComparison.Ordinal))];
        }
    }

    public void Dispose()
    {
        _activeSentinel.Value = _previousSentinel;
        _listener.Dispose();
    }

    private ActivitySamplingResult SampleByAsyncLocal(ref ActivityCreationOptions<ActivityContext> options)
        => IsActiveScope() ? ActivitySamplingResult.AllDataAndRecorded : ActivitySamplingResult.None;

    private ActivitySamplingResult SampleByAsyncLocalParentId(ref ActivityCreationOptions<string> options)
        => IsActiveScope() ? ActivitySamplingResult.AllDataAndRecorded : ActivitySamplingResult.None;

    private void OnActivityStarted(Activity activity)
    {
        // ActivityStarted fires on the creating thread, where AsyncLocal still reflects the
        // creator's flow. We claim ownership here; ActivityStopped (which may run on another
        // thread, e.g. for activities whose using-block returns from a different continuation)
        // checks ownership without needing the AsyncLocal to still match.
        if (IsActiveScope())
        {
            _ours.TryAdd(GetKey(activity), 0);
        }
    }

    private void OnActivityStopped(Activity activity)
    {
        if (_ours.TryRemove(GetKey(activity), out _))
        {
            lock (_lock)
            {
                _captured.Add(activity);
            }
        }
    }

    private bool IsActiveScope() => ReferenceEquals(_activeSentinel.Value, _sentinel);

    private static string GetKey(Activity activity)
    {
        // Activity.Id is W3C-formatted ("00-<traceid>-<spanid>-<flags>") and unique per activity.
        // Falls back to the SpanId hex if Id is null (legacy non-W3C formats — unlikely in our code).
        return activity.Id ?? activity.SpanId.ToHexString();
    }
}
