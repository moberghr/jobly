namespace Warp.Tests.TestData;

// Default 10s — individual tests should finish in seconds, not half-minutes. Tests whose
// purpose is to exercise slow behavior (multi-job integration workloads, retry chains with
// deliberate delays) opt in with an explicit larger timeout, e.g. [TimedFact(60_000)].
// A default in the tens-of-seconds range hides hangs — a stuck test that shouldn't be stuck
// quietly eats its entire timeout instead of failing fast with the real error.
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class TimedFactAttribute : FactAttribute
{
    public TimedFactAttribute(int timeout = 10_000)
    {
        Timeout = timeout;
    }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class TimedTheoryAttribute : TheoryAttribute
{
    public TimedTheoryAttribute(int timeout = 10_000)
    {
        Timeout = timeout;
    }
}
