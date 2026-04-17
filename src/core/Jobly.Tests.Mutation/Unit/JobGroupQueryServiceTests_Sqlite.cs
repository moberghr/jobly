using Jobly.Tests.Mutation.Fixtures;
using Jobly.Tests.Unit;

namespace Jobly.Tests.Mutation.Unit;

[Collection<SqliteCollection>]
public class JobGroupQueryServiceTests_Sqlite : JobGroupQueryServiceTestsBase
{
    public JobGroupQueryServiceTests_Sqlite(SqliteFixture fixture)
        : base(fixture)
    {
    }
}
