using Jobly.Tests.Mutation.Fixtures;
using Jobly.Tests.Unit;

namespace Jobly.Tests.Mutation.Unit;

[Collection<SqliteCollection>]
public class WorkerIdLogTests_Sqlite : WorkerIdLogTestsBase
{
    public WorkerIdLogTests_Sqlite(SqliteFixture fixture)
        : base(fixture)
    {
    }
}
