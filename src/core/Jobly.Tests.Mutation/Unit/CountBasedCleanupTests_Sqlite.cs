using Jobly.Tests.Mutation.Fixtures;
using Jobly.Tests.Unit;

namespace Jobly.Tests.Mutation.Unit;

[Collection<SqliteCollection>]
public class CountBasedCleanupTests_Sqlite : CountBasedCleanupTestsBase
{
    public CountBasedCleanupTests_Sqlite(SqliteFixture fixture)
        : base(fixture)
    {
    }
}
