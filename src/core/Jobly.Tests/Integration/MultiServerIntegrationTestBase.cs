using Jobly.Tests.Fixtures;

namespace Jobly.Tests.Integration;

public abstract class MultiServerIntegrationTestBase : IAsyncLifetime
{
    private readonly IMultiServerDatabaseFixture _fixture;

    protected MultiServerIntegrationTestBase(IMultiServerDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    protected JoblyTestServer Server1 => _fixture.Server1;

    protected JoblyTestServer Server2 => _fixture.Server2;

    protected TestContext CreateContext() => _fixture.CreateContext();

    public async ValueTask InitializeAsync()
    {
        try
        {
            await _fixture.ResetAsync();
        }
        catch
        {
            await Task.Delay(100);
            await _fixture.ResetAsync();
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
