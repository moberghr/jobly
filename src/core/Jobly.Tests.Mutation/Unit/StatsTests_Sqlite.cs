using Jobly.Tests.Mutation.Fixtures;
using Jobly.Tests.Unit;

namespace Jobly.Tests.Mutation.Unit;

[Collection<SqliteCollection>]
public class StatsTests_Sqlite : StatsTestsBase
{
    public StatsTests_Sqlite(SqliteFixture fixture)
        : base(fixture)
    {
    }
}
