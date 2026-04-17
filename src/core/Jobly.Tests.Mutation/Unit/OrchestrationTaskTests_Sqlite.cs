using Jobly.Tests.Mutation.Fixtures;
using Jobly.Tests.Unit;

namespace Jobly.Tests.Mutation.Unit;

[Collection<SqliteCollection>]
public class OrchestrationTaskTests_Sqlite : OrchestrationTaskTestsBase
{
    public OrchestrationTaskTests_Sqlite(SqliteFixture fixture)
        : base(fixture)
    {
    }
}
