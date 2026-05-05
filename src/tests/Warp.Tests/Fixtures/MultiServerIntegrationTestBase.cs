using Warp.Tests.Fixtures;

namespace Warp.Tests.Fixtures;

public abstract class MultiServerIntegrationTestBase : IAsyncLifetime
{
    protected MultiServerIntegrationTestBase(IMultiServerDatabaseFixture fixture)
    {
        Fixture = fixture;
    }

    protected IMultiServerDatabaseFixture Fixture { get; }

    protected WarpTestServer Server1 => Fixture.Server1;

    protected WarpTestServer Server2 => Fixture.Server2;

    protected TestContext CreateContext() => Fixture.CreateContext();

    public virtual async ValueTask InitializeAsync()
    {
        try
        {
            await Fixture.ResetAsync();
        }
        catch
        {
            await Task.Delay(100, Xunit.TestContext.Current.CancellationToken);
            await Fixture.ResetAsync();
        }
    }

    public virtual ValueTask DisposeAsync()
        => FixtureDiagnostics.DumpOnFailureAsync(Fixture.DumpDiagnosticsAsync);
}
