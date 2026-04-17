using Jobly.Tests.Mutation.Fixtures;
using Jobly.Tests.Unit;

namespace Jobly.Tests.Mutation.Unit;

[Collection<SqliteCollection>]
public class ActivityTraceTests_Sqlite : ActivityTraceTestsBase
{
    public ActivityTraceTests_Sqlite(SqliteFixture fixture)
        : base(fixture)
    {
    }
}
