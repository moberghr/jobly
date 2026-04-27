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

    public async ValueTask DisposeAsync()
    {
        // On any non-passing result, dump test-server state to stderr so a flake
        // in CI is diagnosable instead of opaque. WaitForCompletion's own timeout
        // path can't fire here — xunit's [TimedFact] cancels the polling token
        // before WaitForCompletion's deadline branch is reached, so the test
        // exits via OperationCanceledException, not TimeoutException. This hook
        // catches that path (and any other failure) using a fresh CancellationToken
        // since xunit's is already cancelled.
        var testState = Xunit.TestContext.Current.TestState;
        if (testState == null || testState.Result == Xunit.TestResult.Passed)
        {
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var dump = await Server.DumpDiagnosticsAsync(
                $"Test failed ({testState.Result}). Server-state diagnostics:",
                cts.Token);
            await Console.Error.WriteLineAsync(dump);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Diagnostic dump failed: {ex.Message}");
        }
    }
}
