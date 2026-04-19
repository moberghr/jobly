using Jobly.Tests.Fixtures;

namespace Jobly.Tests.Fixtures;

public interface IDatabaseFixture
{
    TestContext CreateContext();

    Task ResetAsync();

    JoblyTestServer? TestServer { get; }
}
