using Jobly.Tests.Mutation.Fixtures;
using Jobly.Tests.Unit;

namespace Jobly.Tests.Mutation.Unit;

[Collection<SqliteCollection>]
public class BatchPublisherUnitTests_Sqlite : BatchPublisherUnitTestsBase
{
    public BatchPublisherUnitTests_Sqlite(SqliteFixture fixture)
        : base(fixture)
    {
    }
}
