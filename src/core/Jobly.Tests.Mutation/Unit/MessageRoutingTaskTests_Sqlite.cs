using Jobly.Tests.Mutation.Fixtures;
using Jobly.Tests.Unit;

namespace Jobly.Tests.Mutation.Unit;

[Collection<SqliteCollection>]
public class MessageRoutingTaskTests_Sqlite : MessageRoutingTaskTestsBase
{
    public MessageRoutingTaskTests_Sqlite(SqliteFixture fixture)
        : base(fixture)
    {
    }
}
