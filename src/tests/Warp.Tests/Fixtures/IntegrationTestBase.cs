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
