using Jobly.Tests.Mutation.Fixtures;
using Jobly.Tests.Unit;

namespace Jobly.Tests.Mutation.Unit;

[Collection<SqliteCollection>]
public class FailedJobTypeFilterTests_Sqlite : FailedJobTypeFilterTestsBase
{
    public FailedJobTypeFilterTests_Sqlite(SqliteFixture fixture)
        : base(fixture)
    {
    }
}
