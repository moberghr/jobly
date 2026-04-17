using Jobly.Tests.Mutation.Fixtures;
using Jobly.Tests.Unit;

namespace Jobly.Tests.Mutation.Unit;

[Collection<SqliteCollection>]
public class RecurringJobBugTests_Sqlite : RecurringJobBugTestsBase
{
    public RecurringJobBugTests_Sqlite(SqliteFixture fixture)
        : base(fixture)
    {
    }
}
