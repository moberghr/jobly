using Jobly.Tests.Mutation.Fixtures;
using Jobly.Tests.Unit;

namespace Jobly.Tests.Mutation.Unit;

[Collection<SqliteCollection>]
public class ServerQueryTests_Sqlite : ServerQueryTestsBase
{
    public ServerQueryTests_Sqlite(SqliteFixture fixture)
        : base(fixture)
    {
    }
}
