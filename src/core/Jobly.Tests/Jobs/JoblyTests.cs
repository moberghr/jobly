namespace Jobly.Tests.Jobs;

public abstract partial class JoblyTests : TestBase
{
    protected JoblyTests(Func<TestContext> createContext)
        : base(createContext)
    {
    }
}
