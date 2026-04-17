using Jobly.Tests.Mutation.Fixtures;
using Jobly.Tests.Unit;

namespace Jobly.Tests.Mutation.Unit;

[Collection<SqliteCollection>]
public class CancellationModeTests_Sqlite : CancellationModeTestsBase
{
    public CancellationModeTests_Sqlite(SqliteFixture fixture)
        : base(fixture)
    {
    }
}
