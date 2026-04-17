using Jobly.Tests.Mutation.Fixtures;
using Jobly.Tests.Unit;

namespace Jobly.Tests.Mutation.Unit;

[Collection<SqliteCollection>]
public class RecurringJobLogCleanupTests_Sqlite : RecurringJobLogCleanupTestsBase
{
    public RecurringJobLogCleanupTests_Sqlite(SqliteFixture fixture)
        : base(fixture)
    {
    }
}
