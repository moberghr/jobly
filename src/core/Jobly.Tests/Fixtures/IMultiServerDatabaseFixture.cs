using Jobly.Tests.Integration;

namespace Jobly.Tests.Fixtures;

public interface IMultiServerDatabaseFixture
{
    TestContext CreateContext();

    Task ResetAsync();

    JoblyTestServer Server1 { get; }

    JoblyTestServer Server2 { get; }
}
