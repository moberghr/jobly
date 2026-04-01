using System.Collections.Concurrent;
using Jobly.Tests.Fixtures;

namespace Jobly.Tests.Integration;

public abstract class IntegrationTestBase : IAsyncLifetime
{
    private static readonly ConcurrentDictionary<Type, JoblyTestServer> _servers = new();
    private readonly IDatabaseFixture _fixture;
    protected JoblyTestServer _server = null!;

    protected IntegrationTestBase(IDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        var key = _fixture.GetType();
        if (!_servers.TryGetValue(key, out var server))
        {
            server = await JoblyTestServer.StartAsync(_fixture);
            _servers[key] = server;
        }

        await _fixture.ResetAsync();
        _server = server;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}
