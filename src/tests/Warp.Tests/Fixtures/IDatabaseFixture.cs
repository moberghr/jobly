using Warp.Tests.Fixtures;

namespace Warp.Tests.Fixtures;

public interface IDatabaseFixture
{
    TestContext CreateContext();

    Task ResetAsync();

    WarpTestServer? TestServer { get; }
}
