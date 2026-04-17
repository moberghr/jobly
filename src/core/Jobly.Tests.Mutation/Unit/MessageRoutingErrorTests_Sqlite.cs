using Jobly.Tests.Mutation.Fixtures;
using Jobly.Tests.Unit;

namespace Jobly.Tests.Mutation.Unit;

[Collection<SqliteCollection>]
public class MessageRoutingErrorTests_Sqlite : MessageRoutingErrorTestsBase
{
    public MessageRoutingErrorTests_Sqlite(SqliteFixture fixture)
        : base(fixture)
    {
    }
}
