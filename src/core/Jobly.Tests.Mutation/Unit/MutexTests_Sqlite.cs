using Jobly.Tests.Mutation.Fixtures;
using Jobly.Tests.Unit;

namespace Jobly.Tests.Mutation.Unit;

[Collection<SqliteCollection>]
public class MutexTests_Sqlite : MutexTestsBase
{
    public MutexTests_Sqlite(SqliteFixture fixture)
        : base(fixture)
    {
    }
}
