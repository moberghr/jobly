using Jobly.Tests.Mutation.Fixtures;
using Jobly.Tests.Unit;

namespace Jobly.Tests.Mutation.Unit;

[Collection<SqliteCollection>]
public class StatCounterTests_Sqlite : StatCounterTestsBase
{
    public StatCounterTests_Sqlite(SqliteFixture fixture)
        : base(fixture)
    {
    }
}
