using Jobly.Tests.Mutation.Fixtures;
using Jobly.Tests.Unit;

namespace Jobly.Tests.Mutation.Unit;

[Collection<SqliteCollection>]
public class PublisherOverloadTests_Sqlite : PublisherOverloadTestsBase
{
    public PublisherOverloadTests_Sqlite(SqliteFixture fixture)
        : base(fixture)
    {
    }
}
