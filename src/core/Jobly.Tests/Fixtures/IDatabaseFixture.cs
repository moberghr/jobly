using Jobly.Tests.Integration;

namespace Jobly.Tests.Fixtures;

public interface IDatabaseFixture
{
    TestContext CreateContext();

    Task ResetAsync();

    JoblyTestServer? TestServer { get; }
}
