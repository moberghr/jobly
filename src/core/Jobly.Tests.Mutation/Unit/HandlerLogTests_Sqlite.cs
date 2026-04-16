using Jobly.Tests.Mutation.Fixtures;
using Jobly.Tests.Unit;

namespace Jobly.Tests.Mutation.Unit;

[Collection<SqliteCollection>]
public class HandlerLogTests_Sqlite : HandlerLogTestsBase
{
    public HandlerLogTests_Sqlite(SqliteFixture fixture)
        : base(fixture)
    {
    }
}
