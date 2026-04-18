namespace Jobly.Tests.TestData;

// Default 30s — integration tests frequently use WaitForJobState(..., 15..30s) for retry
// chains, stale-recovery polls, and batch orchestration; the previous 10s default silently
// produced TimedFact-vs-inner-timeout mismatches (test cancelled at 10s while WaitForJobState
// still had 5s left). Unit tests that pass in <1s are unaffected — the timeout is a cap,
// not a target.
//
// Trade-off: a newly-introduced unit test with a hang/deadlock now takes up to 30s to surface
// in CI instead of 10s. That is the cost of eliminating flaky timeout mismatches across the
// integration suite. Tests that need a shorter fail-fast cap (e.g. JoblyWorkerResilienceTests
// verifying no-hang behavior) pass an explicit smaller timeout.
public class TimedFactAttribute : FactAttribute
{
    public TimedFactAttribute(int timeout = 30_000)
    {
        Timeout = timeout;
    }
}

public class TimedTheoryAttribute : TheoryAttribute
{
    public TimedTheoryAttribute(int timeout = 30_000)
    {
        Timeout = timeout;
    }
}
