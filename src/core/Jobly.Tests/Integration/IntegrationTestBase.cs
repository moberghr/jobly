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
        _fixture.TestServer ??= await JoblyTestServer.StartAsync(_fixture);
        await _fixture.ResetAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;
}
