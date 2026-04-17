using Jobly.Tests.Mutation.Fixtures;
using Jobly.Tests.Unit;

namespace Jobly.Tests.Mutation.Unit;

[Collection<SqliteCollection>]
public class JobQueryServiceTests_Sqlite : JobQueryServiceTestsBase
{
    public JobQueryServiceTests_Sqlite(SqliteFixture fixture)
        : base(fixture)
    {
    }
}
