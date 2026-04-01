using System.Collections.Concurrent;
using Jobly.Tests.Fixtures;

namespace Jobly.Tests.Integration;

public abstract class IntegrationTestBase : IAsyncLifetime
{
    private static readonly ConcurrentDictionary<Type, JoblyTestServer> _servers = new();
    private static readonly SemaphoreSlim _lock = new(1, 1);
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
            await _lock.WaitAsync();
            try
            {
                if (!_servers.TryGetValue(key, out server))
                {
                    server = await JoblyTestServer.StartAsync(_fixture);
                    _servers[key] = server;
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        await _fixture.ResetAsync();
        _server = server;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}
