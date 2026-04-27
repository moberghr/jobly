using Warp.Tests.Fixtures;

namespace Warp.Tests.Fixtures;

public abstract class MultiServerIntegrationTestBase : IAsyncLifetime
{
    private readonly IMultiServerDatabaseFixture _fixture;

    protected MultiServerIntegrationTestBase(IMultiServerDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    protected WarpTestServer Server1 => _fixture.Server1;

    protected WarpTestServer Server2 => _fixture.Server2;

    protected TestContext CreateContext() => _fixture.CreateContext();

    public async ValueTask InitializeAsync()
    {
        try
        {
            await _fixture.ResetAsync();
        }
        catch
        {
            await Task.Delay(100, Xunit.TestContext.Current.CancellationToken);
            await _fixture.ResetAsync();
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
