using Jobly.Tests.Mutation.Fixtures;
using Jobly.Tests.Unit;

namespace Jobly.Tests.Mutation.Unit;

[Collection<SqliteCollection>]
public class PriorityTests_Sqlite : PriorityTestsBase
{
    public PriorityTests_Sqlite(SqliteFixture fixture)
        : base(fixture)
    {
    }
}
