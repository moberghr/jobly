using Jobly.Tests.Mutation.Fixtures;
using Jobly.Tests.Unit;

namespace Jobly.Tests.Mutation.Unit;

[Collection<SqliteCollection>]
public class RetryTests_Sqlite : RetryTestsBase
{
    public RetryTests_Sqlite(SqliteFixture fixture)
        : base(fixture)
    {
    }
}
