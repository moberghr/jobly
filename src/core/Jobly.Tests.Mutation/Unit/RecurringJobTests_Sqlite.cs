using Jobly.Tests.Mutation.Fixtures;
using Jobly.Tests.Unit;

namespace Jobly.Tests.Mutation.Unit;

[Collection<SqliteCollection>]
public class RecurringJobTests_Sqlite : RecurringJobTestsBase
{
    public RecurringJobTests_Sqlite(SqliteFixture fixture)
        : base(fixture)
    {
    }
}
