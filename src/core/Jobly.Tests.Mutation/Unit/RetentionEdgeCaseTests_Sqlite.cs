using Jobly.Tests.Mutation.Fixtures;
using Jobly.Tests.Unit;

namespace Jobly.Tests.Mutation.Unit;

[Collection<SqliteCollection>]
public class RetentionEdgeCaseTests_Sqlite : RetentionEdgeCaseTestsBase
{
    public RetentionEdgeCaseTests_Sqlite(SqliteFixture fixture)
        : base(fixture)
    {
    }
}
