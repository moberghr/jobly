using Jobly.Tests.Fixtures;

namespace Jobly.Tests.Integration;

public abstract class IntegrationTestBase : IAsyncLifetime
{
    protected readonly IDatabaseFixture _fixture;

    protected IntegrationTestBase(IDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    protected JoblyTestServer Server => _fixture.TestServer!;

    public async Task InitializeAsync()
    {
        await _fixture.ResetAsync();
        await Server.ReRegisterServer();
    }

    public Task DisposeAsync() => Task.CompletedTask;
}
