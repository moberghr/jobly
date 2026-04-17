using Jobly.Tests.Mutation.Fixtures;
using Jobly.Tests.Unit;

namespace Jobly.Tests.Mutation.Unit;

[Collection<SqliteCollection>]
public class BugFixTests_Sqlite : BugFixTestsBase
{
    public BugFixTests_Sqlite(SqliteFixture fixture)
        : base(fixture)
    {
    }
}
