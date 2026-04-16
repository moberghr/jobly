using Jobly.Tests.Mutation.Fixtures;
using Jobly.Tests.Unit;

namespace Jobly.Tests.Mutation.Unit;

[Collection<SqliteCollection>]
public class BackgroundTaskTests_Sqlite : BackgroundTaskTestsBase
{
    public BackgroundTaskTests_Sqlite(SqliteFixture fixture)
        : base(fixture)
    {
    }
}
