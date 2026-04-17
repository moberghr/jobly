using Jobly.Worker;
using Shouldly;

namespace Jobly.Tests.Unit;

public class PauseStateHolderTests
{
    [TimedFact]
    public void IsPaused_ReturnsFalse_WhenNothingPaused()
    {
        var holder = new PauseStateHolder();
        var groupId = Guid.NewGuid();

        holder.IsPaused(groupId).ShouldBeFalse();
        holder.IsPaused(null).ShouldBeFalse();
    }

    [TimedFact]
    public void IsPaused_ReturnsTrue_WhenServerPaused()
    {
        var holder = new PauseStateHolder();
        var groupId = Guid.NewGuid();

        holder.Update(true, new Dictionary<Guid, bool> { { groupId, false } });

        holder.IsPaused(groupId).ShouldBeTrue();
        holder.IsPaused(null).ShouldBeTrue();
    }

    [TimedFact]
    public void IsPaused_ReturnsTrue_WhenGroupPaused()
    {
        var holder = new PauseStateHolder();
        var pausedGroup = Guid.NewGuid();
        var runningGroup = Guid.NewGuid();

        holder.Update(false, new Dictionary<Guid, bool>
        {
            { pausedGroup, true },
            { runningGroup, false },
        });

        holder.IsPaused(pausedGroup).ShouldBeTrue();
        holder.IsPaused(runningGroup).ShouldBeFalse();
        holder.IsPaused(null).ShouldBeFalse();
    }

    [TimedFact]
    public void IsPaused_ReturnsTrue_WhenBothPaused()
    {
        var holder = new PauseStateHolder();
        var groupId = Guid.NewGuid();

        holder.Update(true, new Dictionary<Guid, bool> { { groupId, true } });

        holder.IsPaused(groupId).ShouldBeTrue();
    }

    [TimedFact]
    public void Update_ReplacesState()
    {
        var holder = new PauseStateHolder();
        var groupId = Guid.NewGuid();

        holder.Update(true, new Dictionary<Guid, bool> { { groupId, true } });
        holder.IsPaused(groupId).ShouldBeTrue();

        holder.Update(false, new Dictionary<Guid, bool> { { groupId, false } });
        holder.IsPaused(groupId).ShouldBeFalse();
    }

    [TimedFact]
    public void IsPaused_ReturnsFalse_OnFreshInstance()
    {
        // A fresh PauseStateHolder should default to "not paused".
        // This documents that workers starting before the first heartbeat
        // will see "not paused" — the startup initialization must populate
        // the holder from DB before workers begin.
        var holder = new PauseStateHolder();
        var groupId = Guid.NewGuid();
        holder.IsPaused(groupId).ShouldBeFalse();
    }

    /// <summary>
    /// Verifies that concurrent Update + IsPaused never sees a torn state.
    /// The writer alternates between two states where IsPaused(groupId) is always true.
    /// If the reader ever sees false, the snapshot was torn (non-atomic write).
    ///
    /// State A: server=true,  group=false → IsPaused=true  (server overrides)
    /// State B: server=false, group=true  → IsPaused=true  (group is paused)
    /// Torn:    server=false, group=false → IsPaused=FALSE (bug!)
    /// </summary>
    [TimedFact]
    public async Task Update_IsAtomic_NeverExposesTornState()
    {
        var holder = new PauseStateHolder();
        var groupId = Guid.NewGuid();

        // Start in state A
        holder.Update(true, new Dictionary<Guid, bool> { { groupId, false } });

        var tornReadsDetected = 0;
        var readerDone = false;
        var iterations = 0;
        using var readerStarted = new ManualResetEventSlim(false);

        // Reader: continuously check IsPaused — should always be true
        var readerTask = Task.Run(() =>
        {
            readerStarted.Set();
            while (!Volatile.Read(ref readerDone))
            {
                if (!holder.IsPaused(groupId))
                {
                    Interlocked.Increment(ref tornReadsDetected);
                }

                Interlocked.Increment(ref iterations);
            }
        });

        // Wait for reader to start before writing, so it doesn't starve under thread pool contention
        readerStarted.Wait();

        // Writer: rapidly alternate between state A and state B
        for (var i = 0; i < 100_000; i++)
        {
            if (i % 2 == 0)
            {
                // State B: server=false, group=true
                holder.Update(false, new Dictionary<Guid, bool> { { groupId, true } });
            }
            else
            {
                // State A: server=true, group=false
                holder.Update(true, new Dictionary<Guid, bool> { { groupId, false } });
            }
        }

        Volatile.Write(ref readerDone, true);
        await readerTask;

        iterations.ShouldBeGreaterThan(0, "Reader task did not execute");
        tornReadsDetected.ShouldBe(0, $"Detected {tornReadsDetected} torn reads out of {iterations} iterations");
    }
}
