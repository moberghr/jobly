using Microsoft.EntityFrameworkCore;
using Shouldly;
using Warp.Core.Entities;
using Warp.Core.Enums;

namespace Warp.Tests.Fixtures;

[GenerateDatabaseTests]
public abstract class DiagnosticDumpCaptureTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected DiagnosticDumpCaptureTestsBase(IDatabaseFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync()
    {
        await _fixture.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [TimedFact]
    public async Task DisposeAsync_StashesLiveStateBeforeStopAsync_IncludingSeededJobRows()
    {
        // Initialize must happen in the same async flow as the WarpTestServer disposal and the
        // subsequent Drain. AsyncLocal Box writes from inside `await server.DisposeAsync()`
        // mutate the shared box reference, but only if the box was created in an ancestor
        // frame. Calling Initialize at the test-method scope makes that ancestor THIS frame.
        DiagnosticDumpStorage.Initialize();

        // Server alive throughout setup; we seed a row whose state would be visible BEFORE
        // StopAsync but might be deleted/cleaned by StopAsync teardown. The dump-before-disposal
        // capture must observe the row in its pre-stop state.
        var server = await WarpTestServer.StartAsync(_fixture);

        var jobId = Guid.NewGuid();
        var ctx = _fixture.CreateContext();
        ctx.Set<Job>().Add(new Job
        {
            Id = jobId,
            Kind = JobKind.Job,
            CurrentState = State.Scheduled,
            Queue = "default",
            Type = "TestType",
            Message = "{}",
            CreateTime = DateTime.UtcNow,

            // Far-future ScheduleTime so neither ScheduledJobActivation nor a worker can
            // touch it during the brief window this test holds the server alive.
            ScheduleTime = DateTime.UtcNow.AddHours(1),
        });
        await ctx.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken);

        await server.DisposeAsync();

        var snapshot = DiagnosticDumpStorage.Drain();
        snapshot.ShouldNotBeNull();
        snapshot.ShouldContain(jobId.ToString());
        snapshot.ShouldContain("Pre-disposal server-state diagnostics");

        // Cross-check that this isn't accidentally the post-disposal fall-back path: the
        // header text distinguishes them, and the row must show as Scheduled (terminal-state
        // workers can't reach it because ScheduleTime is far in the future).
        snapshot.ShouldContain("Scheduled");
    }

    [TimedFact]
    public async Task DisposeAsync_StashIsDrainedByDumpOnFailureAsync()
    {
        // The stash box must be created in this test-method's async frame so the
        // WarpTestServer disposal's mutation flows back here.
        DiagnosticDumpStorage.Initialize();

        var server = await WarpTestServer.StartAsync(_fixture);
        await server.DisposeAsync();

        DiagnosticDumpStorage.Drain().ShouldNotBeNull("server disposal should stash a snapshot");
        DiagnosticDumpStorage.Drain().ShouldBeNull("second drain should observe an empty slot");
    }
}
