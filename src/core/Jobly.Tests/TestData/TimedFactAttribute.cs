namespace Jobly.Tests.TestData;

public class TimedFactAttribute : FactAttribute
{
    public TimedFactAttribute(int timeout = 10_000)
    {
        Timeout = timeout;
    }
}

public class TimedTheoryAttribute : TheoryAttribute
{
    public TimedTheoryAttribute(int timeout = 10_000)
    {
        Timeout = timeout;
    }
}
