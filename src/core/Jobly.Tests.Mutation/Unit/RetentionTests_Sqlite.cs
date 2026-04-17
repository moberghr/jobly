using Jobly.Tests.Mutation.Fixtures;
using Jobly.Tests.Unit;

namespace Jobly.Tests.Mutation.Unit;

[Collection<SqliteCollection>]
public class RetentionTests_Sqlite : RetentionTestsBase
{
    public RetentionTests_Sqlite(SqliteFixture fixture)
        : base(fixture)
    {
    }
}
