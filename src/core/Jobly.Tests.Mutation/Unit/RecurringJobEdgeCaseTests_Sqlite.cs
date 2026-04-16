using Jobly.Tests.Mutation.Fixtures;
using Jobly.Tests.Unit;

namespace Jobly.Tests.Mutation.Unit;

[Collection<SqliteCollection>]
public class RecurringJobEdgeCaseTests_Sqlite : RecurringJobEdgeCaseTestsBase
{
    public RecurringJobEdgeCaseTests_Sqlite(SqliteFixture fixture)
        : base(fixture)
    {
    }
}
