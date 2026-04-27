using Warp.Tests.Fixtures;

namespace Warp.Tests.Fixtures;

public abstract class IntegrationTestBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;

    protected IntegrationTestBase(IDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    protected WarpTestServer Server => _fixture.TestServer!;

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
