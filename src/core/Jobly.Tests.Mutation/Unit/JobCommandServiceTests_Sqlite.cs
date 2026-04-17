using Jobly.Tests.Mutation.Fixtures;
using Jobly.Tests.Unit;

namespace Jobly.Tests.Mutation.Unit;

[Collection<SqliteCollection>]
public class JobCommandServiceTests_Sqlite : JobCommandServiceTestsBase
{
    public JobCommandServiceTests_Sqlite(SqliteFixture fixture)
        : base(fixture)
    {
    }
}
