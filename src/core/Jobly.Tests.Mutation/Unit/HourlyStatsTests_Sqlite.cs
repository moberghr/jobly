using Jobly.Tests.Mutation.Fixtures;
using Jobly.Tests.Unit;

namespace Jobly.Tests.Mutation.Unit;

[Collection<SqliteCollection>]
public class HourlyStatsTests_Sqlite : HourlyStatsTestsBase
{
    public HourlyStatsTests_Sqlite(SqliteFixture fixture)
        : base(fixture)
    {
    }
}
