using System.Text;
using Microsoft.EntityFrameworkCore;
using Warp.Core.Data.Entities;
using Warp.Core.Entities;
using Warp.Core.Enums;

namespace Warp.Tests.Fixtures;

/// <summary>
/// Builds a multi-line diagnostic dump of test-server state — stuck Job rows with their
/// JobLog tail, every ServerTask row's last run, and the most recent ServerLog entries.
/// Lives on the fixture (not WarpTestServer) because it's a pure DB read; it doesn't matter
/// which server published the rows. Callers pass their own <paramref name="ct"/> so the
/// failure-path call can use a fresh token instead of xunit's already-cancelled one.
/// </summary>
public static class FixtureDiagnostics
{
    public static Task<string> DumpDiagnosticsAsync(this IDatabaseFixture fixture, string header, CancellationToken ct)
        => DumpAsync(fixture.CreateContext(), header, ct);

    /// <summary>
    /// Shared failure-dump tail for integration test bases. On any non-passing test result,
    /// invokes <paramref name="dumper"/> with a fresh CancellationToken (xunit's is already
    /// cancelled) and writes the result to stderr so flakes are diagnosable in CI.
    /// </summary>
    public static async ValueTask DumpOnFailureAsync(Func<string, CancellationToken, Task<string>> dumper)
    {
        var testState = Xunit.TestContext.Current.TestState;
        if (testState == null || testState.Result == Xunit.TestResult.Passed)
        {
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var dump = await dumper(
                $"Test failed ({testState.Result}). Server-state diagnostics:",
                cts.Token);
            await Console.Error.WriteLineAsync(dump);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Diagnostic dump failed: {ex.Message}");
        }
    }

    private static async Task<string> DumpAsync(TestContext debugCtx, string header, CancellationToken ct)
    {
        var stuckJobs = await debugCtx.Set<Job>()
            .AsNoTracking()
            .Where(j => j.CurrentState == State.Enqueued || j.CurrentState == State.Processing
                || j.CurrentState == State.Awaiting || j.CurrentState == State.Scheduled)
            .OrderBy(j => j.CreateTime)
            .Select(j =>
                new { j.Id, j.Kind, j.CurrentState, j.ParentJobId, j.Queue, j.ScheduleTime, j.CurrentWorkerId, j.LastKeepAlive })
            .Take(20)
            .ToListAsync(ct);

        var stuckIds = stuckJobs.ConvertAll(j => j.Id);
        var stuckLogs = stuckIds.Count == 0
            ? []
            : await debugCtx.Set<JobLog>()
                .AsNoTracking()
                .Where(l => stuckIds.Contains(l.JobId))
                .OrderBy(l => l.Timestamp)
                .Select(l =>
                    new { l.JobId, l.Timestamp, l.EventType, l.Level, l.Message })
                .ToListAsync(ct);

        var serverTasks = await debugCtx.Set<ServerTask>()
            .AsNoTracking()
            .OrderBy(t => t.TaskName)
            .Select(t =>
                new { t.TaskName, t.IntervalSeconds, t.LastRun, t.LastStatus, t.LastMessage, t.LastDurationMs })
            .ToListAsync(ct);

        var serverLogs = await debugCtx.Set<ServerLog>()
            .AsNoTracking()
            .OrderByDescending(l => l.Timestamp)
            .Take(30)
            .Select(l =>
                new { l.Timestamp, l.Status, l.Message, l.DurationMs, TaskName = l.ServerTask != null ? l.ServerTask.TaskName : null })
            .ToListAsync(ct);

        var lifecycleEvents = ServerLifecycleTrace.Drain();

        var sb = new StringBuilder();
        sb.AppendLine(header);

        sb.AppendLine();
        sb.AppendLine($"Server lifecycle ({lifecycleEvents.Count} events):");
        foreach (var grouped in lifecycleEvents.GroupBy(e => e.ServerId))
        {
            sb.AppendLine($"  Server {grouped.Key}:");
            foreach (var e in grouped.OrderBy(x => x.Timestamp))
            {
                sb.AppendLine($"    [{e.Timestamp:HH:mm:ss.fff}] {e.Event}");
            }
        }

        sb.AppendLine();
        sb.AppendLine($"Stuck jobs ({stuckJobs.Count}):");
        foreach (var j in stuckJobs)
        {
            sb.AppendLine($"  {j.Id} kind={j.Kind} state={j.CurrentState} queue={j.Queue} parent={j.ParentJobId} scheduleTime={j.ScheduleTime:HH:mm:ss.fff} worker={j.CurrentWorkerId} keepAlive={j.LastKeepAlive:HH:mm:ss.fff}");
            foreach (var l in stuckLogs.Where(x => x.JobId == j.Id))
            {
                sb.AppendLine($"    [{l.Timestamp:HH:mm:ss.fff}] {l.Level} {l.EventType} — {l.Message}");
            }
        }

        sb.AppendLine();
        sb.AppendLine($"ServerTask rows ({serverTasks.Count}):");
        foreach (var t in serverTasks)
        {
            sb.AppendLine($"  {t.TaskName} interval={t.IntervalSeconds}s lastRun={t.LastRun:HH:mm:ss.fff} status={t.LastStatus} duration={t.LastDurationMs:0.#}ms message={t.LastMessage}");
        }

        sb.AppendLine();
        sb.AppendLine($"Recent ServerLog entries ({serverLogs.Count}, newest first):");
        foreach (var l in serverLogs)
        {
            sb.AppendLine($"  [{l.Timestamp:HH:mm:ss.fff}] {l.TaskName ?? "<no-task>"} {l.Status} {l.DurationMs:0.#}ms — {l.Message}");
        }

        return sb.ToString();
    }
}
