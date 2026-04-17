using Jobly.Tests.Mutation.Fixtures;
using Jobly.Tests.Unit;

namespace Jobly.Tests.Mutation.Unit;

[Collection<SqliteCollection>]
public class DashboardBreakdownTests_Sqlite : DashboardBreakdownTestsBase
{
    public DashboardBreakdownTests_Sqlite(SqliteFixture fixture)
        : base(fixture)
    {
    }
}
