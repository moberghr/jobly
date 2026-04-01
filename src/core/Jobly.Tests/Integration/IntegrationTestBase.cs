using Jobly.Tests.Fixtures;

namespace Jobly.Tests.Integration;

public abstract class IntegrationTestBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;
    protected JoblyTestServer _server = null!;

    protected IntegrationTestBase(IDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _fixture.TestServer ??= await JoblyTestServer.StartAsync(_fixture);
        await _fixture.ResetAsync();
        _server = _fixture.TestServer;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}
