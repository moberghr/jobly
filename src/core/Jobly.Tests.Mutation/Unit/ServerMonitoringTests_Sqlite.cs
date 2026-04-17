using Jobly.Tests.Mutation.Fixtures;
using Jobly.Tests.Unit;

namespace Jobly.Tests.Mutation.Unit;

[Collection<SqliteCollection>]
public class ServerMonitoringTests_Sqlite : ServerMonitoringTestsBase
{
    public ServerMonitoringTests_Sqlite(SqliteFixture fixture)
        : base(fixture)
    {
    }
}
