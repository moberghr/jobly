using Jobly.Tests.Mutation.Fixtures;
using Jobly.Tests.Unit;

namespace Jobly.Tests.Mutation.Unit;

[Collection<SqliteCollection>]
public class RecurringJobLogCascadeTests_Sqlite : RecurringJobLogCascadeTestsBase
{
    public RecurringJobLogCascadeTests_Sqlite(SqliteFixture fixture)
        : base(fixture)
    {
    }
}
