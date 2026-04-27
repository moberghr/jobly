using Warp.Tests.Fixtures;

namespace Warp.Tests.Fixtures;

public interface IMultiServerDatabaseFixture
{
    TestContext CreateContext();

    Task ResetAsync();

    WarpTestServer Server1 { get; }

    WarpTestServer Server2 { get; }
}
