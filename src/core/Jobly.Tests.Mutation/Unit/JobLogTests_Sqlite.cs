using Jobly.Tests.Mutation.Fixtures;
using Jobly.Tests.Unit;

namespace Jobly.Tests.Mutation.Unit;

[Collection<SqliteCollection>]
public class JobLogTests_Sqlite : JobLogTestsBase
{
    public JobLogTests_Sqlite(SqliteFixture fixture)
        : base(fixture)
    {
    }
}
