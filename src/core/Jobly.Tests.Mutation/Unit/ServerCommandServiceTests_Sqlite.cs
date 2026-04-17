using Jobly.Tests.Mutation.Fixtures;
using Jobly.Tests.Unit;

namespace Jobly.Tests.Mutation.Unit;

[Collection<SqliteCollection>]
public class ServerCommandServiceTests_Sqlite : ServerCommandServiceTestsBase
{
    public ServerCommandServiceTests_Sqlite(SqliteFixture fixture)
        : base(fixture)
    {
    }
}
