using Jobly.Tests.Mutation.Fixtures;
using Jobly.Tests.Unit;

namespace Jobly.Tests.Mutation.Unit;

[Collection<SqliteCollection>]
public class RequeueEdgeCaseTests_Sqlite : RequeueEdgeCaseTestsBase
{
    public RequeueEdgeCaseTests_Sqlite(SqliteFixture fixture)
        : base(fixture)
    {
    }
}
