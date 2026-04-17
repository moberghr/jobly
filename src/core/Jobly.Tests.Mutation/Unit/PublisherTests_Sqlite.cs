using Jobly.Tests.Mutation.Fixtures;
using Jobly.Tests.Unit;

namespace Jobly.Tests.Mutation.Unit;

[Collection<SqliteCollection>]
public class PublisherTests_Sqlite : PublisherTestsBase
{
    public PublisherTests_Sqlite(SqliteFixture fixture)
        : base(fixture)
    {
    }
}
