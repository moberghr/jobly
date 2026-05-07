using Warp.Tests.Fixtures;

namespace Warp.Tests.Fixtures;

public abstract class IntegrationTestBase : IAsyncLifetime
{
    protected IntegrationTestBase(IDatabaseFixture fixture)
    {
        Fixture = fixture;
    }

    protected IDatabaseFixture Fixture { get; }

    public virtual async ValueTask InitializeAsync()
    {
        TestLifecycleTrace.Record("IntegrationTestBase.InitializeAsync starting");
        try
        {
            TestLifecycleTrace.Record("Fixture.ResetAsync starting");
            await Fixture.ResetAsync();
            TestLifecycleTrace.Record("Fixture.ResetAsync returned");
        }
        catch
        {
            TestLifecycleTrace.Record("Fixture.ResetAsync threw, retrying");
            await Task.Delay(100, Xunit.TestContext.Current.CancellationToken);
            await Fixture.ResetAsync();
            TestLifecycleTrace.Record("Fixture.ResetAsync returned (retry)");
        }

        TestLifecycleTrace.Record("IntegrationTestBase.InitializeAsync returned");
    }

    public virtual ValueTask DisposeAsync()
        => FixtureDiagnostics.DumpOnFailureAsync(Fixture.DumpDiagnosticsAsync);
}
